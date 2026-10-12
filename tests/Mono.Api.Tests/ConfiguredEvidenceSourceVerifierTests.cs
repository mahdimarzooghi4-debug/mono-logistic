using System.Globalization;
using Microsoft.Extensions.Configuration;
using Mono.Api.Domain;
using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class ConfiguredEvidenceSourceVerifierTests
{
    private const string CustomerId = "cus_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string SourceId = "maps-internal";
    private const string KeyId = "rotation-1";
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Key = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();

    [Fact]
    public async Task Hana_distance_requires_enabled_tenant_source_scope_freshness_and_valid_hmac()
    {
        var observedAt = Now.AddMinutes(-1);
        var sourcePayload = HanaDistancePricingEvidenceContract.ComputeSourcePayloadSha256(
            "evidence-1", observedAt, 1_250m, SourceId);
        var attestation = EvidenceSourceAttestationContract.Create(
            CustomerId, SourceId, "hana-distance-pricing", "evidence-1",
            observedAt, sourcePayload, KeyId, Key);
        var evidence = HanaDistancePricingEvidenceContract.Attach(
            "evidence-1", observedAt, 1_250m, SourceId, attestation);
        var verifier = CreateVerifier("hana-distance-pricing");

        var result = await verifier.VerifyAsync(CustomerId, evidence);

        Assert.Equal(HanaDistanceEvidenceVerificationStatus.Verified, result.Status);
        Assert.Equal("SOURCE_ATTESTATION_VERIFIED", result.ReasonCode);
    }

    [Fact]
    public async Task Missing_global_enablement_remains_fail_closed()
    {
        var evidence = CreateHanaEvidence(Now.AddMinutes(-1));
        var verifier = CreateVerifier("hana-distance-pricing", enabled: false);

        var result = await verifier.VerifyAsync(CustomerId, evidence);

        Assert.Equal(HanaDistanceEvidenceVerificationStatus.VerifierNotConfigured, result.Status);
    }

    [Fact]
    public async Task Source_is_not_trusted_for_another_customer_or_unlisted_scope()
    {
        var evidence = CreateHanaEvidence(Now.AddMinutes(-1));
        var verifier = CreateVerifier("feasibility:Distance");

        var foreignTenant = await verifier.VerifyAsync(
            "cus_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", evidence);
        var wrongScope = await verifier.VerifyAsync(CustomerId, evidence);

        Assert.Equal(HanaDistanceEvidenceVerificationStatus.VerifierNotConfigured, foreignTenant.Status);
        Assert.Equal(HanaDistanceEvidenceVerificationStatus.Unverified, wrongScope.Status);
        Assert.Equal("SOURCE_SCOPE_NOT_ALLOWED", wrongScope.ReasonCode);
    }

    [Fact]
    public async Task Stale_evidence_is_rejected_before_signature_acceptance()
    {
        var evidence = CreateHanaEvidence(Now.AddHours(-2));
        var verifier = CreateVerifier("hana-distance-pricing", maxAgeSeconds: 300);

        var result = await verifier.VerifyAsync(CustomerId, evidence);

        Assert.Equal(HanaDistanceEvidenceVerificationStatus.Unverified, result.Status);
        Assert.Equal("SOURCE_EVIDENCE_STALE", result.ReasonCode);
    }

    [Fact]
    public async Task Future_observations_beyond_configured_clock_skew_are_rejected()
    {
        var evidence = CreateHanaEvidence(Now.AddMinutes(5));
        var verifier = CreateVerifier("hana-distance-pricing");

        var result = await verifier.VerifyAsync(CustomerId, evidence);

        Assert.Equal(HanaDistanceEvidenceVerificationStatus.Unverified, result.Status);
        Assert.Equal("SOURCE_OBSERVATION_TIME_INVALID", result.ReasonCode);
    }

    [Fact]
    public async Task Invalid_signature_is_rejected_even_when_integrity_digest_is_recomputed()
    {
        var valid = CreateHanaEvidence(Now.AddMinutes(-1));
        var alteredSignature = valid.SourceAttestation[..^1] + (valid.SourceAttestation[^1] == 'A' ? "B" : "A");
        var altered = HanaDistancePricingEvidenceContract.Attach(
            valid.EvidenceId, valid.ObservedAt, valid.DistanceMeters, valid.SourceId, alteredSignature);
        var verifier = CreateVerifier("hana-distance-pricing");

        var result = await verifier.VerifyAsync(CustomerId, altered);

        Assert.True(HanaDistancePricingEvidenceContract.IsValid(altered));
        Assert.Equal(HanaDistanceEvidenceVerificationStatus.Unverified, result.Status);
        Assert.Equal("SOURCE_ATTESTATION_INVALID", result.ReasonCode);
    }

    [Fact]
    public async Task Feasibility_attestation_is_bound_to_criterion_result_and_tenant()
    {
        var observedAt = Now.AddSeconds(-10);
        var result = new FeasibilityCriterionResult(
            FeasibilityCriterion.Distance,
            FeasibilityCriterionStatus.Pass,
            ["DISTANCE_WITHIN_LIMIT"]);
        var sourcePayload = FeasibilityEvidenceIntegrityContract.ComputeSourcePayloadSha256(
            result, "distance-criterion-1", observedAt, SourceId);
        var attestation = EvidenceSourceAttestationContract.Create(
            CustomerId, SourceId, "feasibility:Distance", "distance-criterion-1",
            observedAt, sourcePayload, KeyId, Key);
        var attached = FeasibilityEvidenceIntegrityContract.Attach(
            result, "distance-criterion-1", observedAt, SourceId, attestation);
        var verifier = CreateVerifier("feasibility:Distance");

        var verified = await verifier.VerifyAsync(CustomerId, FeasibilityCriterion.Distance, attached.Integrity!);
        var wrongCriterion = await verifier.VerifyAsync(CustomerId, FeasibilityCriterion.Coverage, attached.Integrity!);

        Assert.True(FeasibilityEvidenceIntegrityContract.IsValid(attached));
        Assert.Equal(FeasibilityEvidenceSourceVerificationStatus.Verified, verified.Status);
        Assert.Equal(FeasibilityEvidenceSourceVerificationStatus.Unverified, wrongCriterion.Status);
    }

    [Fact]
    public async Task Invalid_or_duplicate_key_configuration_fails_closed()
    {
        var evidence = CreateHanaEvidence(Now.AddMinutes(-1));
        var verifier = CreateVerifier("hana-distance-pricing", duplicateKey: true);

        var result = await verifier.VerifyAsync(CustomerId, evidence);

        Assert.Equal(HanaDistanceEvidenceVerificationStatus.Unverified, result.Status);
        Assert.Equal("SOURCE_CONFIGURATION_AMBIGUOUS", result.ReasonCode);
    }

    private static HanaDistancePricingEvidence CreateHanaEvidence(DateTimeOffset observedAt)
    {
        var sourcePayload = HanaDistancePricingEvidenceContract.ComputeSourcePayloadSha256(
            "evidence-1", observedAt, 1_250m, SourceId);
        var attestation = EvidenceSourceAttestationContract.Create(
            CustomerId, SourceId, "hana-distance-pricing", "evidence-1",
            observedAt, sourcePayload, KeyId, Key);
        return HanaDistancePricingEvidenceContract.Attach(
            "evidence-1", observedAt, 1_250m, SourceId, attestation);
    }

    private static ConfiguredEvidenceSourceVerifier CreateVerifier(
        string scope,
        bool enabled = true,
        int maxAgeSeconds = 3_600,
        bool duplicateKey = false)
    {
        var values = new Dictionary<string, string?>
        {
            ["Mono:EvidenceSources:Enabled"] = enabled.ToString(),
            ["Mono:EvidenceSources:TrustedSources:0:CustomerId"] = CustomerId,
            ["Mono:EvidenceSources:TrustedSources:0:SourceId"] = SourceId,
            ["Mono:EvidenceSources:TrustedSources:0:KeyId"] = KeyId,
            ["Mono:EvidenceSources:TrustedSources:0:KeyBase64"] = Convert.ToBase64String(Key),
            ["Mono:EvidenceSources:TrustedSources:0:Enabled"] = "true",
            ["Mono:EvidenceSources:TrustedSources:0:MaxEvidenceAgeSeconds"] = maxAgeSeconds.ToString(CultureInfo.InvariantCulture),
            ["Mono:EvidenceSources:TrustedSources:0:AllowedFutureClockSkewSeconds"] = "30",
            ["Mono:EvidenceSources:TrustedSources:0:Scopes:0"] = scope
        };
        if (duplicateKey)
        {
            values["Mono:EvidenceSources:TrustedSources:1:CustomerId"] = CustomerId;
            values["Mono:EvidenceSources:TrustedSources:1:SourceId"] = SourceId;
            values["Mono:EvidenceSources:TrustedSources:1:KeyId"] = KeyId;
            values["Mono:EvidenceSources:TrustedSources:1:KeyBase64"] = Convert.ToBase64String(Key);
            values["Mono:EvidenceSources:TrustedSources:1:Enabled"] = "true";
            values["Mono:EvidenceSources:TrustedSources:1:MaxEvidenceAgeSeconds"] = maxAgeSeconds.ToString(CultureInfo.InvariantCulture);
            values["Mono:EvidenceSources:TrustedSources:1:AllowedFutureClockSkewSeconds"] = "30";
            values["Mono:EvidenceSources:TrustedSources:1:Scopes:0"] = scope;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ConfiguredEvidenceSourceVerifier(configuration, new FixedTimeProvider(Now));
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
