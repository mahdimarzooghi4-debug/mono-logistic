using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public static class DeliveryQuoteContract
{
    private static readonly Regex CustomerIdPattern = new(
        @"\Acus_[0-9a-f]{32}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DeliveryRequestIdPattern = new(
        @"\Adrq_[0-9a-f]{32}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void Validate(DeliveryQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);

        if (!HasText(quote.QuoteId, 128))
            throw new ArgumentException("Quote ID is required.", nameof(quote));
        if (!CustomerIdPattern.IsMatch(quote.CustomerId ?? string.Empty))
            throw new ArgumentException("Quote customer ID must be a valid Mono customer ID.", nameof(quote));
        if (!DeliveryRequestIdPattern.IsMatch(quote.DeliveryRequestId ?? string.Empty))
            throw new ArgumentException("Quote delivery request ID must be a valid Mono delivery request ID.", nameof(quote));
        if (quote.Version < 1)
            throw new ArgumentException("Quote version must be positive.", nameof(quote));
        if (quote.IssuedAt == default || quote.ExpiresAt == default || quote.ExpiresAt <= quote.IssuedAt)
            throw new ArgumentException("Quote expiration must follow its issue time.", nameof(quote));

        ValidateFare(quote.Fare, nameof(quote));
        ValidatePolicyVersions(quote.PolicyVersions, quote.Fare.PolicyVersion, nameof(quote));
        ValidateAuditEvidence(quote.AuditEvidenceIds, nameof(quote));
    }

    public static bool IsExpired(DeliveryQuote quote, DateTimeOffset evaluatedAt)
    {
        Validate(quote);
        if (evaluatedAt == default)
            throw new ArgumentException("Evaluation time is required.", nameof(evaluatedAt));

        return evaluatedAt >= quote.ExpiresAt;
    }

    private static void ValidateFare(HanaDistanceFare? fare, string parameterName)
    {
        if (fare is null)
            throw new ArgumentException("A calculated Hana fare is required.", parameterName);

        var expected = HanaDistancePricingV1.Calculate(fare.DistanceMeters);
        if (fare.PolicyVersion != expected.PolicyVersion ||
            fare.DistanceBandStartMeters != expected.DistanceBandStartMeters ||
            fare.FareToman != expected.FareToman ||
            fare.BuyerShareToman != expected.BuyerShareToman ||
            fare.SellerShareToman != expected.SellerShareToman)
        {
            throw new ArgumentException("Fare does not match the Hana tariff for its distance.", parameterName);
        }
    }

    private static void ValidatePolicyVersions(
        IReadOnlyDictionary<string, string>? policyVersions,
        string pricingPolicyVersion,
        string parameterName)
    {
        if (policyVersions is null ||
            policyVersions.Count == 0 ||
            policyVersions.Any(entry => !HasText(entry.Key, 64) || !HasText(entry.Value, 128)) ||
            !policyVersions.TryGetValue("pricing", out var recordedPricingVersion) ||
            !string.Equals(recordedPricingVersion, pricingPolicyVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Quote policy versions must include the matching pricing policy version.",
                parameterName);
        }
    }

    private static void ValidateAuditEvidence(IReadOnlyList<string>? evidenceIds, string parameterName)
    {
        if (evidenceIds is null ||
            evidenceIds.Count == 0 ||
            evidenceIds.Any(id => !HasText(id, 128)) ||
            evidenceIds.Distinct(StringComparer.Ordinal).Count() != evidenceIds.Count)
        {
            throw new ArgumentException(
                "Quote requires unique audit evidence identifiers.",
                parameterName);
        }
    }

    private static bool HasText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maxLength &&
        !value.Any(char.IsControl);
}
