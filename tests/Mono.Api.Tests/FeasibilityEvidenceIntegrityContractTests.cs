using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class FeasibilityEvidenceIntegrityContractTests
{
    [Fact]
    public void Digest_is_stable_across_reason_order_and_timestamp_offsets()
    {
        var observedAt = DateTimeOffset.UnixEpoch.AddHours(1);
        var first = FeasibilityEvidenceIntegrityContract.Attach(
            new FeasibilityCriterionResult(
                FeasibilityCriterion.RouteInsertion,
                FeasibilityCriterionStatus.Fail,
                ["ROUTE_DELAY", "SLA_MISSED"]),
            "route-evaluation-1",
            observedAt,
            "test-source",
            "test-attestation");
        var second = FeasibilityEvidenceIntegrityContract.Attach(
            new FeasibilityCriterionResult(
                FeasibilityCriterion.RouteInsertion,
                FeasibilityCriterionStatus.Fail,
                ["SLA_MISSED", "ROUTE_DELAY"]),
            "route-evaluation-1",
            observedAt.ToOffset(TimeSpan.FromHours(3)),
            "test-source",
            "test-attestation");

        Assert.Equal(first.Integrity!.PayloadSha256, second.Integrity!.PayloadSha256);
        Assert.True(FeasibilityEvidenceIntegrityContract.IsValid(first));
    }

    [Fact]
    public void Changes_to_result_or_integrity_metadata_invalidate_digest()
    {
        var result = FeasibilityEvidenceIntegrityContract.Attach(
            new FeasibilityCriterionResult(
                FeasibilityCriterion.SlaFeasibility,
                FeasibilityCriterionStatus.Pass,
                []),
            "sla-evaluation-1",
            DateTimeOffset.UnixEpoch.AddHours(1));

        Assert.False(FeasibilityEvidenceIntegrityContract.IsValid(
            result with { Status = FeasibilityCriterionStatus.Fail }));
        Assert.False(FeasibilityEvidenceIntegrityContract.IsValid(
            result with { Integrity = result.Integrity! with { EvidenceId = "different-evidence" } }));
        Assert.False(FeasibilityEvidenceIntegrityContract.IsValid(
            result with { Integrity = result.Integrity! with { SourceId = "another-source" } }));
        Assert.False(FeasibilityEvidenceIntegrityContract.IsValid(
            result with { Integrity = result.Integrity! with { PayloadSha256 = new string('0', 64) } }));
    }

    [Fact]
    public void Required_set_must_have_one_valid_envelope_per_criterion()
    {
        var all = FeasibilityEvidenceGate.Required
            .Select(criterion => FeasibilityEvidenceIntegrityContract.Attach(
                new FeasibilityCriterionResult(criterion, FeasibilityCriterionStatus.Pass, []),
                $"evidence-{criterion}",
                DateTimeOffset.UnixEpoch.AddHours(1)))
            .ToArray();

        Assert.True(FeasibilityEvidenceIntegrityContract.AreAllValid(all));
        Assert.False(FeasibilityEvidenceIntegrityContract.AreAllValid(all[..^1]));
        Assert.False(FeasibilityEvidenceIntegrityContract.AreAllValid(
            all.Select((item, index) => index == 0 ? all[1] : item).ToArray()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad\nid")]
    public void Invalid_evidence_ids_are_rejected(string evidenceId)
    {
        var result = new FeasibilityCriterionResult(
            FeasibilityCriterion.Coverage,
            FeasibilityCriterionStatus.Pass,
            []);

        Assert.Throws<ArgumentException>(() =>
            FeasibilityEvidenceIntegrityContract.Attach(result, evidenceId, DateTimeOffset.UnixEpoch.AddHours(1)));
    }
}
