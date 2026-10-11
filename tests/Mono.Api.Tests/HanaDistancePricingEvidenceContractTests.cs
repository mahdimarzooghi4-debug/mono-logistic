using Mono.Api.Domain;
using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class HanaDistancePricingEvidenceContractTests
{
    [Fact]
    public void Digest_is_stable_across_timestamp_offsets_and_decimal_scale()
    {
        var observedAt = DateTimeOffset.UnixEpoch.AddHours(1);
        var first = HanaDistancePricingEvidenceContract.Attach(
            "route-distance-1", observedAt, 1_250m, "maps-source", "attestation-1");
        var second = HanaDistancePricingEvidenceContract.Attach(
            "route-distance-1",
            observedAt.ToOffset(TimeSpan.FromHours(3)),
            1_250.00m,
            "maps-source",
            "attestation-1");

        Assert.Equal(first.PayloadSha256, second.PayloadSha256);
        Assert.True(HanaDistancePricingEvidenceContract.IsValid(first));
    }

    [Fact]
    public void Changes_to_distance_or_source_metadata_invalidate_digest()
    {
        var evidence = HanaDistancePricingEvidenceContract.Attach(
            "route-distance-1",
            DateTimeOffset.UnixEpoch.AddHours(1),
            1_250m,
            "maps-source",
            "attestation-1");

        Assert.False(HanaDistancePricingEvidenceContract.IsValid(evidence with { DistanceMeters = 1_251m }));
        Assert.False(HanaDistancePricingEvidenceContract.IsValid(evidence with { EvidenceId = "other-evidence" }));
        Assert.False(HanaDistancePricingEvidenceContract.IsValid(evidence with { SourceId = "other-source" }));
        Assert.False(HanaDistancePricingEvidenceContract.IsValid(
            evidence with { SourceAttestation = "other-attestation" }));
        Assert.False(HanaDistancePricingEvidenceContract.IsValid(
            evidence with { PayloadSha256 = new string('0', 64) }));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(5_001.0)]
    public void Distance_outside_approved_tariff_range_is_rejected(double distanceMeters)
    {
        Assert.Throws<ArgumentException>(() =>
            HanaDistancePricingEvidenceContract.Attach(
                "route-distance-1",
                DateTimeOffset.UnixEpoch.AddHours(1),
                (decimal)distanceMeters,
                "maps-source",
                "attestation-1"));
    }

    [Fact]
    public async Task Unconfigured_source_verifier_fails_closed()
    {
        var evidence = HanaDistancePricingEvidenceContract.Attach(
            "route-distance-1",
            DateTimeOffset.UnixEpoch.AddHours(1),
            1_250m,
            "maps-source",
            "attestation-1");
        var verifier = new UnconfiguredHanaDistanceEvidenceSourceVerifier();

        var result = await verifier.VerifyAsync("cus_0123456789abcdef0123456789abcdef", evidence);

        Assert.Equal(HanaDistanceEvidenceVerificationStatus.VerifierNotConfigured, result.Status);
        Assert.Equal("DISTANCE_SOURCE_VERIFIER_NOT_CONFIGURED", result.ReasonCode);
    }
}
