using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class DeliveryQuoteContractTests
{
    [Fact]
    public void Accepts_quote_with_explicit_expiry_policy_version_and_evidence()
    {
        var quote = Quote();

        DeliveryQuoteContract.Validate(quote);

        Assert.Equal(HanaDistancePricingV1.PolicyVersion, quote.PolicyVersions["pricing"]);
        Assert.Equal(27_500, quote.Fare.FareToman);
    }

    [Fact]
    public void Quote_expires_at_the_exact_expiration_time()
    {
        var quote = Quote();
        var beforeExpiration = quote.ExpiresAt.AddTicks(-1);

        Assert.False(DeliveryQuoteContract.IsExpired(quote, beforeExpiration));
        Assert.True(DeliveryQuoteContract.IsExpired(quote, quote.ExpiresAt));
        Assert.True(DeliveryQuoteContract.IsExpired(quote, quote.ExpiresAt.AddTicks(1)));
    }

    [Fact]
    public void Requires_expiration_after_issue_time_and_positive_version()
    {
        Assert.Throws<ArgumentException>(() =>
            DeliveryQuoteContract.Validate(Quote() with { ExpiresAt = Quote().IssuedAt }));
        Assert.Throws<ArgumentException>(() =>
            DeliveryQuoteContract.Validate(Quote() with { Version = 0 }));
    }

    [Fact]
    public void Requires_pricing_policy_to_match_fare_policy()
    {
        var quote = Quote() with
        {
            PolicyVersions = new Dictionary<string, string> { ["pricing"] = "different-policy" }
        };

        Assert.Throws<ArgumentException>(() => DeliveryQuoteContract.Validate(quote));
    }

    [Fact]
    public void Rejects_fare_that_does_not_match_the_recorded_distance()
    {
        var quote = Quote() with
        {
            Fare = Quote().Fare with { FareToman = 50_000 }
        };

        Assert.Throws<ArgumentException>(() => DeliveryQuoteContract.Validate(quote));
    }

    [Fact]
    public void Requires_unique_audit_evidence_identifiers()
    {
        var quote = Quote() with { AuditEvidenceIds = ["evidence-1", "evidence-1"] };

        Assert.Throws<ArgumentException>(() => DeliveryQuoteContract.Validate(quote));
    }

    [Fact]
    public void Requires_valid_customer_and_delivery_request_scope()
    {
        Assert.Throws<ArgumentException>(() =>
            DeliveryQuoteContract.Validate(Quote() with { CustomerId = "customer-1" }));
        Assert.Throws<ArgumentException>(() =>
            DeliveryQuoteContract.Validate(Quote() with { DeliveryRequestId = "request-1" }));
    }

    [Fact]
    public void Requires_evaluation_time_for_expiration_check()
    {
        Assert.Throws<ArgumentException>(() =>
            DeliveryQuoteContract.IsExpired(Quote(), default));
    }

    private static DeliveryQuote Quote()
    {
        var issuedAt = DateTimeOffset.UnixEpoch.AddHours(1);
        var fare = HanaDistancePricingV1.Calculate(1_500m);
        return new DeliveryQuote(
            "quote-1",
            "cus_11111111111111111111111111111111",
            "drq_22222222222222222222222222222222",
            1,
            issuedAt,
            issuedAt.AddMinutes(15),
            fare,
            new Dictionary<string, string> { ["pricing"] = HanaDistancePricingV1.PolicyVersion },
            ["feasibility-1"]);
    }
}
