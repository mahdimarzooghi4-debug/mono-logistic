using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class DeliveryQuoteIssuanceApplicationServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-11T12:00:00Z");
    private const string CustomerId = "cus_0123456789abcdef0123456789abcdef";
    private const string RequestId = "drq_0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Verified_distance_creates_first_quote_using_server_tariff_and_explicit_expiry()
    {
        var store = new RecordingQuoteStore();
        var verifier = new FixedDistanceVerifier(
            new HanaDistanceEvidenceVerification(HanaDistanceEvidenceVerificationStatus.Verified, "VERIFIED"));
        var service = CreateService(store, verifier);
        var evidence = Evidence();

        var result = await service.IssueInitialAsync(
            RequestId, Now.AddMinutes(15), evidence);

        Assert.Equal(DeliveryQuoteIssuanceStatus.Issued, result.Status);
        Assert.Equal(DeliveryQuoteWriteOutcome.Persisted, result.Persistence!.Outcome);
        Assert.NotNull(store.Quote);
        Assert.Equal(CustomerId, store.Quote.CustomerId);
        Assert.Equal(RequestId, store.Quote.DeliveryRequestId);
        Assert.Equal(1, store.Quote.Version);
        Assert.Equal(Now, store.Quote.IssuedAt);
        Assert.Equal(Now.AddMinutes(15), store.Quote.ExpiresAt);
        Assert.Equal(35_000, store.Quote.Fare.FareToman);
        Assert.Equal(17_500, store.Quote.Fare.BuyerShareToman);
        Assert.Equal(17_500, store.Quote.Fare.SellerShareToman);
        Assert.Equal("distance-proof-1", Assert.Single(store.Quote.AuditEvidenceIds));
        Assert.Equal(evidence.PayloadSha256, store.Quote.PricingEvidence!.PayloadSha256);
        Assert.Equal("maps-source", store.Quote.PricingEvidence.SourceId);
        Assert.Equal(CustomerId, verifier.CustomerId);
    }

    [Fact]
    public async Task Default_unconfigured_verifier_prevents_persistence()
    {
        var store = new RecordingQuoteStore();
        var service = CreateService(store, new UnconfiguredHanaDistanceEvidenceSourceVerifier());

        var result = await service.IssueInitialAsync(RequestId, Now.AddMinutes(15), Evidence());

        Assert.Equal(DeliveryQuoteIssuanceStatus.EvidenceSourceUnverified, result.Status);
        Assert.Null(store.Quote);
    }

    [Fact]
    public async Task Invalid_or_expired_input_is_rejected_before_source_verification_or_persistence()
    {
        var store = new RecordingQuoteStore();
        var verifier = new FixedDistanceVerifier(
            new HanaDistanceEvidenceVerification(HanaDistanceEvidenceVerificationStatus.Verified, "VERIFIED"));
        var service = CreateService(store, verifier);
        var invalidEvidence = Evidence() with { DistanceMeters = 9_000m };

        var invalidEvidenceResult = await service.IssueInitialAsync(
            RequestId, Now.AddMinutes(15), invalidEvidence);
        var expiredResult = await service.IssueInitialAsync(
            RequestId, Now, Evidence());

        Assert.Equal(DeliveryQuoteIssuanceStatus.EvidenceIntegrityInvalid, invalidEvidenceResult.Status);
        Assert.Equal(DeliveryQuoteIssuanceStatus.ExpiryInvalid, expiredResult.Status);
        Assert.Equal(0, verifier.CallCount);
        Assert.Null(store.Quote);
    }

    private static DeliveryQuoteIssuanceApplicationService CreateService(
        RecordingQuoteStore store,
        IHanaDistanceEvidenceSourceVerifier verifier) =>
        new(store, new FixedCustomerContext(CustomerId), verifier, new FixedTimeProvider(Now));

    private static HanaDistancePricingEvidence Evidence() =>
        HanaDistancePricingEvidenceContract.Attach(
            "distance-proof-1",
            Now.AddMinutes(-1),
            2_500m,
            "maps-source",
            "attestation-signature");

    private sealed record FixedCustomerContext(string CustomerId) : ICustomerContext;

    private sealed class FixedDistanceVerifier(HanaDistanceEvidenceVerification result)
        : IHanaDistanceEvidenceSourceVerifier
    {
        public string? CustomerId { get; private set; }
        public int CallCount { get; private set; }

        public Task<HanaDistanceEvidenceVerification> VerifyAsync(
            string customerId,
            HanaDistancePricingEvidence evidence,
            CancellationToken cancellationToken = default)
        {
            CustomerId = customerId;
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class RecordingQuoteStore : IDeliveryQuoteStore
    {
        public DeliveryQuote? Quote { get; private set; }

        public Task<DeliveryQuoteWriteResult> PersistAsync(
            DeliveryQuote quote,
            CancellationToken cancellationToken = default)
        {
            Quote = quote;
            return Task.FromResult(new DeliveryQuoteWriteResult(
                DeliveryQuoteWriteOutcome.Persisted, quote.QuoteId, quote.Version));
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
