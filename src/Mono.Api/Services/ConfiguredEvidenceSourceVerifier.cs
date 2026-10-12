using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Mono.Api.Domain;

namespace Mono.Api.Services;

/// <summary>
/// Source-side contract for a tenant- and scope-bound HMAC attestation.
/// The caller signs the source payload digest before attaching the signature to the final envelope.
/// </summary>
public static class EvidenceSourceAttestationContract
{
    private static readonly Regex KeyIdPattern = new(
        @"\A[A-Za-z0-9_-]{1,64}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DigestPattern = new(
        @"\A[0-9a-f]{64}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string Create(
        string customerId,
        string sourceId,
        string scope,
        string evidenceId,
        DateTimeOffset observedAt,
        string sourcePayloadSha256,
        string keyId,
        byte[] key)
    {
        ValidateInputs(customerId, sourceId, scope, evidenceId, observedAt, sourcePayloadSha256, keyId, key);
        var signature = HMACSHA256.HashData(
            key,
            Encoding.UTF8.GetBytes(CanonicalMessage(
                customerId, sourceId, scope, evidenceId, observedAt, sourcePayloadSha256)));
        return "mono-hmac-sha256.v1." + keyId + "." + ToBase64Url(signature);
    }

    public static bool Verify(
        string customerId,
        string sourceId,
        string scope,
        string evidenceId,
        DateTimeOffset observedAt,
        string sourcePayloadSha256,
        string attestation,
        byte[] key,
        out string keyId)
    {
        keyId = string.Empty;
        if (string.IsNullOrWhiteSpace(attestation) || attestation.Length > 256 ||
            string.IsNullOrWhiteSpace(customerId) || customerId.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 128 || sourceId.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(scope) || scope.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(evidenceId) || evidenceId.Any(char.IsControl) ||
            observedAt == default ||
            key is null || key.Length < 32 ||
            !DigestPattern.IsMatch(sourcePayloadSha256 ?? string.Empty))
            return false;

        var parts = attestation.Split('.', StringSplitOptions.None);
        if (parts.Length != 4 ||
            parts[0] != "mono-hmac-sha256" ||
            parts[1] != "v1" ||
            !KeyIdPattern.IsMatch(parts[2]) ||
            parts[3].Length != 43 ||
            parts[3].Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            return false;

        keyId = parts[2];
        byte[] supplied;
        try
        {
            supplied = FromBase64Url(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (supplied.Length != 32)
            return false;

        byte[] expected;
        try
        {
            expected = HMACSHA256.HashData(
                key,
                Encoding.UTF8.GetBytes(CanonicalMessage(
                    customerId, sourceId, scope, evidenceId, observedAt, sourcePayloadSha256)));
        }
        catch (ArgumentException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }

    private static string CanonicalMessage(
        string customerId,
        string sourceId,
        string scope,
        string evidenceId,
        DateTimeOffset observedAt,
        string sourcePayloadSha256) =>
        string.Join("\n",
            "mono-evidence-source-attestation-v1",
            customerId,
            sourceId,
            scope,
            evidenceId,
            observedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            sourcePayloadSha256);

    private static void ValidateInputs(
        string customerId,
        string sourceId,
        string scope,
        string evidenceId,
        DateTimeOffset observedAt,
        string sourcePayloadSha256,
        string keyId,
        byte[] key)
    {
        if (string.IsNullOrWhiteSpace(customerId) || customerId.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 128 ||
            sourceId.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(scope) || scope.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(evidenceId) || evidenceId.Any(char.IsControl) ||
            observedAt == default ||
            !DigestPattern.IsMatch(sourcePayloadSha256 ?? string.Empty) ||
            !KeyIdPattern.IsMatch(keyId ?? string.Empty) ||
            key is null || key.Length < 32)
        {
            throw new ArgumentException("A valid source identity, evidence digest, key ID, and 256-bit key are required.");
        }
    }

    private static string ToBase64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
        return Convert.FromBase64String(base64);
    }
}

/// <summary>
/// Uses an explicitly enabled, tenant-scoped configuration registry. No sources or keys
/// are trusted by default; missing, ambiguous, stale, or malformed configuration fails closed.
/// </summary>
public sealed class ConfiguredEvidenceSourceVerifier : IFeasibilityEvidenceSourceVerifier, IHanaDistanceEvidenceSourceVerifier
{
    private const string ConfigurationPath = "Mono:EvidenceSources";
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;

    public ConfiguredEvidenceSourceVerifier(IConfiguration configuration, TimeProvider timeProvider)
    {
        _configuration = configuration;
        _timeProvider = timeProvider;
    }

    public Task<FeasibilityEvidenceSourceVerification> VerifyAsync(
        string customerId,
        FeasibilityCriterion criterion,
        FeasibilityEvidenceIntegrity integrity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(criterion) ||
            integrity is null ||
            string.IsNullOrWhiteSpace(integrity.SourceId) ||
            string.IsNullOrWhiteSpace(integrity.SourceAttestation) ||
            string.IsNullOrWhiteSpace(integrity.SourcePayloadSha256))
        {
            return Task.FromResult(new FeasibilityEvidenceSourceVerification(
                FeasibilityEvidenceSourceVerificationStatus.Unverified,
                "SOURCE_ATTESTATION_INVALID"));
        }

        var scope = "feasibility:" + criterion;
        var result = Verify(
            customerId,
            integrity.SourceId,
            scope,
            integrity.EvidenceId,
            integrity.ObservedAt,
            integrity.SourcePayloadSha256,
            integrity.SourceAttestation);
        return Task.FromResult(new FeasibilityEvidenceSourceVerification(result.Status, result.ReasonCode));
    }

    public Task<HanaDistanceEvidenceVerification> VerifyAsync(
        string customerId,
        HanaDistancePricingEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HanaDistancePricingEvidenceContract.IsValid(evidence))
        {
            return Task.FromResult(new HanaDistanceEvidenceVerification(
                HanaDistanceEvidenceVerificationStatus.Unverified,
                "DISTANCE_EVIDENCE_INTEGRITY_INVALID"));
        }

        var sourcePayloadSha256 = HanaDistancePricingEvidenceContract.ComputeSourcePayloadSha256(
            evidence.EvidenceId, evidence.ObservedAt, evidence.DistanceMeters, evidence.SourceId);
        var result = Verify(
            customerId,
            evidence.SourceId,
            "hana-distance-pricing",
            evidence.EvidenceId,
            evidence.ObservedAt,
            sourcePayloadSha256,
            evidence.SourceAttestation);
        var status = result.Status switch
        {
            FeasibilityEvidenceSourceVerificationStatus.Verified => HanaDistanceEvidenceVerificationStatus.Verified,
            FeasibilityEvidenceSourceVerificationStatus.Unverified => HanaDistanceEvidenceVerificationStatus.Unverified,
            _ => HanaDistanceEvidenceVerificationStatus.VerifierNotConfigured
        };
        var reasonCode = result.ReasonCode == "SOURCE_VERIFIER_NOT_CONFIGURED"
            ? "DISTANCE_SOURCE_VERIFIER_NOT_CONFIGURED"
            : result.ReasonCode;
        return Task.FromResult(new HanaDistanceEvidenceVerification(status, reasonCode));
    }

    private FeasibilityEvidenceSourceVerification Verify(
        string customerId,
        string sourceId,
        string scope,
        string evidenceId,
        DateTimeOffset observedAt,
        string sourcePayloadSha256,
        string attestation)
    {
        if (!string.Equals(
                _configuration[ConfigurationPath + ":Enabled"],
                "true",
                StringComparison.OrdinalIgnoreCase))
            return NotConfigured("SOURCE_VERIFIER_NOT_CONFIGURED");

        if (!EvidenceSourceAttestationParsing.TryReadKeyId(attestation, out var keyId))
            return Unverified("SOURCE_ATTESTATION_INVALID");

        var sources = _configuration.GetSection(ConfigurationPath + ":TrustedSources")
            .GetChildren()
            .Where(entry =>
                string.Equals(entry["CustomerId"], customerId, StringComparison.Ordinal) &&
                string.Equals(entry["SourceId"], sourceId, StringComparison.Ordinal))
            .ToArray();
        if (sources.Length == 0)
            return NotConfigured("SOURCE_NOT_REGISTERED");

        var keyEntries = sources.Where(entry =>
                string.Equals(entry["KeyId"], keyId, StringComparison.Ordinal))
            .ToArray();
        if (keyEntries.Length == 0)
            return Unverified("SOURCE_KEY_NOT_ACTIVE");
        if (keyEntries.Length != 1)
            return Unverified("SOURCE_CONFIGURATION_AMBIGUOUS");

        var keyEntry = keyEntries[0];
        if (!string.Equals(keyEntry["Enabled"], "true", StringComparison.OrdinalIgnoreCase))
            return Unverified("SOURCE_KEY_NOT_ACTIVE");

        var scopeAllowed = keyEntry.GetSection("Scopes").GetChildren()
            .Any(value => string.Equals(value.Value, scope, StringComparison.Ordinal));
        if (!scopeAllowed)
            return Unverified("SOURCE_SCOPE_NOT_ALLOWED");

        if (!int.TryParse(keyEntry["MaxEvidenceAgeSeconds"], NumberStyles.None, CultureInfo.InvariantCulture, out var maxAgeSeconds) ||
            maxAgeSeconds <= 0 ||
            !int.TryParse(keyEntry["AllowedFutureClockSkewSeconds"], NumberStyles.None, CultureInfo.InvariantCulture, out var futureSkewSeconds) ||
            futureSkewSeconds < 0)
            return NotConfigured("SOURCE_FRESHNESS_POLICY_NOT_CONFIGURED");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(keyEntry["KeyBase64"] ?? string.Empty);
        }
        catch (FormatException)
        {
            return NotConfigured("SOURCE_KEY_CONFIGURATION_INVALID");
        }

        if (key.Length < 32)
        {
            CryptographicOperations.ZeroMemory(key);
            return NotConfigured("SOURCE_KEY_CONFIGURATION_INVALID");
        }

        var now = _timeProvider.GetUtcNow();
        if (observedAt == default ||
            observedAt - now > TimeSpan.FromSeconds(futureSkewSeconds))
        {
            CryptographicOperations.ZeroMemory(key);
            return Unverified("SOURCE_OBSERVATION_TIME_INVALID");
        }

        if (now - observedAt > TimeSpan.FromSeconds(maxAgeSeconds))
        {
            CryptographicOperations.ZeroMemory(key);
            return Unverified("SOURCE_EVIDENCE_STALE");
        }

        var verified = EvidenceSourceAttestationContract.Verify(
            customerId, sourceId, scope, evidenceId, observedAt, sourcePayloadSha256, attestation, key, out _);
        CryptographicOperations.ZeroMemory(key);
        return verified
            ? new FeasibilityEvidenceSourceVerification(
                FeasibilityEvidenceSourceVerificationStatus.Verified, "SOURCE_ATTESTATION_VERIFIED")
            : Unverified("SOURCE_ATTESTATION_INVALID");
    }

    private static FeasibilityEvidenceSourceVerification NotConfigured(string reason) =>
        new(FeasibilityEvidenceSourceVerificationStatus.VerifierNotConfigured, reason);

    private static FeasibilityEvidenceSourceVerification Unverified(string reason) =>
        new(FeasibilityEvidenceSourceVerificationStatus.Unverified, reason);
}

/// <summary>Parsing helper shared by the verifier and its contract tests.</summary>
public static class EvidenceSourceAttestationParsing
{
    public static bool TryReadKeyId(string? attestation, out string keyId)
    {
        keyId = string.Empty;
        if (string.IsNullOrWhiteSpace(attestation) || attestation.Length > 256)
            return false;
        var parts = attestation.Split('.', StringSplitOptions.None);
        if (parts.Length != 4 ||
            parts[0] != "mono-hmac-sha256" ||
            parts[1] != "v1" ||
            !Regex.IsMatch(parts[2], @"\A[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant) ||
            parts[3].Length != 43 ||
            parts[3].Any(character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '-' or '_')))
            return false;
        keyId = parts[2];
        return true;
    }
}
