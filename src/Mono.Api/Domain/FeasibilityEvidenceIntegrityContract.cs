using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public sealed record FeasibilityEvidenceIntegrity(
    string EvidenceId,
    DateTimeOffset ObservedAt,
    string PayloadSha256);

/// <summary>
/// Detects changes to a criterion result after its integrity envelope is created.
/// The digest does not authenticate the source or establish that the evidence is true.
/// </summary>
public static class FeasibilityEvidenceIntegrityContract
{
    private static readonly Regex DigestPattern = new(
        @"\A[0-9a-f]{64}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReasonCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static FeasibilityCriterionResult Attach(
        FeasibilityCriterionResult result,
        string evidenceId,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(result);
        ValidatePayload(result, nameof(result));
        if (!HasText(evidenceId, 128) || observedAt == default)
            throw new ArgumentException("Evidence ID and observation time are required.");

        var normalizedObservedAt = observedAt.ToUniversalTime();
        var digest = ComputeDigest(result, evidenceId, normalizedObservedAt);
        return result with
        {
            Integrity = new FeasibilityEvidenceIntegrity(evidenceId, normalizedObservedAt, digest)
        };
    }

    public static bool IsValid(FeasibilityCriterionResult? result)
    {
        if (result is null || result.Integrity is not { } integrity ||
            !HasText(integrity.EvidenceId, 128) ||
            integrity.ObservedAt == default ||
            !DigestPattern.IsMatch(integrity.PayloadSha256 ?? string.Empty))
        {
            return false;
        }

        try
        {
            ValidatePayload(result, nameof(result));
            var expected = Convert.FromHexString(
                ComputeDigest(result, integrity.EvidenceId, integrity.ObservedAt.ToUniversalTime()));
            var actual = Convert.FromHexString(integrity.PayloadSha256);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static bool AreAllValid(IReadOnlyCollection<FeasibilityCriterionResult>? results)
    {
        if (results is null || results.Count != FeasibilityEvidenceGate.Required.Count)
            return false;

        var seen = new HashSet<FeasibilityCriterion>();
        foreach (var result in results)
        {
            if (result is null ||
                !seen.Add(result.Criterion) ||
                !IsValid(result))
            {
                return false;
            }
        }

        return FeasibilityEvidenceGate.Required.All(seen.Contains);
    }

    private static string ComputeDigest(
        FeasibilityCriterionResult result,
        string evidenceId,
        DateTimeOffset observedAt)
    {
        var canonicalPayload = JsonSerializer.Serialize(new
        {
            criterion = result.Criterion.ToString(),
            status = result.Status.ToString(),
            reason_codes = result.ReasonCodes.Order(StringComparer.Ordinal).ToArray(),
            evidence_id = evidenceId,
            observed_at = observedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)))
            .ToLowerInvariant();
    }

    private static void ValidatePayload(FeasibilityCriterionResult result, string parameterName)
    {
        if (!Enum.IsDefined(result.Criterion) ||
            !Enum.IsDefined(result.Status) ||
            result.ReasonCodes is null ||
            result.ReasonCodes.Any(code => string.IsNullOrWhiteSpace(code) || !ReasonCodePattern.IsMatch(code)) ||
            result.ReasonCodes.Distinct(StringComparer.Ordinal).Count() != result.ReasonCodes.Count)
        {
            throw new ArgumentException("Criterion payload is malformed.", parameterName);
        }
    }

    private static bool HasText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maxLength &&
        !value.Any(char.IsControl);
}
