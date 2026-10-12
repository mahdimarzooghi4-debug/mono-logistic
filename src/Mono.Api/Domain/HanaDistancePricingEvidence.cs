using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public sealed record HanaDistancePricingEvidence(
    string EvidenceId,
    DateTimeOffset ObservedAt,
    decimal DistanceMeters,
    string SourceId,
    string SourceAttestation,
    string PayloadSha256);

/// <summary>
/// Binds a measured distance and its source metadata to a stable digest.
/// The digest detects mutation; it does not authenticate the source or prove the distance.
/// </summary>
public static class HanaDistancePricingEvidenceContract
{
    private static readonly Regex DigestPattern = new(
        @"\A[0-9a-f]{64}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static HanaDistancePricingEvidence Attach(
        string evidenceId,
        DateTimeOffset observedAt,
        decimal distanceMeters,
        string sourceId,
        string sourceAttestation)
    {
        ValidateFields(evidenceId, observedAt, distanceMeters, sourceId, sourceAttestation, nameof(evidenceId));
        var normalizedObservedAt = observedAt.ToUniversalTime();
        var digest = ComputeDigest(
            evidenceId, normalizedObservedAt, distanceMeters, sourceId, sourceAttestation);
        return new HanaDistancePricingEvidence(
            evidenceId, normalizedObservedAt, distanceMeters, sourceId, sourceAttestation, digest);
    }

    public static string ComputeSourcePayloadSha256(
        string evidenceId,
        DateTimeOffset observedAt,
        decimal distanceMeters,
        string sourceId)
    {
        ValidateFields(evidenceId, observedAt, distanceMeters, sourceId, null, nameof(evidenceId));
        var canonicalPayload = JsonSerializer.Serialize(new
        {
            evidence_id = evidenceId,
            observed_at = observedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            distance_meters = distanceMeters.ToString("G29", CultureInfo.InvariantCulture),
            source_id = sourceId
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)))
            .ToLowerInvariant();
    }

    public static bool IsValid(HanaDistancePricingEvidence? evidence)
    {
        if (evidence is null ||
            evidence.PayloadSha256 is null ||
            !DigestPattern.IsMatch(evidence.PayloadSha256))
        {
            return false;
        }

        try
        {
            ValidateFields(
                evidence.EvidenceId,
                evidence.ObservedAt,
                evidence.DistanceMeters,
                evidence.SourceId,
                evidence.SourceAttestation,
                nameof(evidence));
            var expected = Convert.FromHexString(ComputeDigest(
                evidence.EvidenceId,
                evidence.ObservedAt.ToUniversalTime(),
                evidence.DistanceMeters,
                evidence.SourceId,
                evidence.SourceAttestation));
            var actual = Convert.FromHexString(evidence.PayloadSha256);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string ComputeDigest(
        string evidenceId,
        DateTimeOffset observedAt,
        decimal distanceMeters,
        string sourceId,
        string sourceAttestation)
    {
        var canonicalPayload = JsonSerializer.Serialize(new
        {
            evidence_id = evidenceId,
            observed_at = observedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            distance_meters = distanceMeters.ToString("G29", CultureInfo.InvariantCulture),
            source_id = sourceId,
            source_attestation = sourceAttestation
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)))
            .ToLowerInvariant();
    }

    private static void ValidateFields(
        string? evidenceId,
        DateTimeOffset observedAt,
        decimal distanceMeters,
        string? sourceId,
        string? sourceAttestation,
        string parameterName)
    {
        if (!HasText(evidenceId, 128) ||
            observedAt == default ||
            distanceMeters < 0 ||
            distanceMeters > HanaDistancePricingV1.MaximumDistanceMeters ||
            !HasText(sourceId, 128) ||
            (sourceAttestation is not null && !HasText(sourceAttestation, 4_096)))
        {
            throw new ArgumentException(
                "Distance evidence requires a valid ID, observation time, in-range distance, source ID, and attestation.",
                parameterName);
        }
    }

    private static bool HasText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maxLength &&
        !value.Any(char.IsControl);
}
