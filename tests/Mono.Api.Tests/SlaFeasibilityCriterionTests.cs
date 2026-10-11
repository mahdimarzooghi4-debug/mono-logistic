using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class SlaFeasibilityCriterionTests
{
    [Fact]
    public void EstimateBeforeDeadlinePasses()
    {
        var result = SlaFeasibilityCriterion.Evaluate(Evidence(
            DateTimeOffset.UnixEpoch.AddMinutes(20),
            DateTimeOffset.UnixEpoch.AddMinutes(30)));

        Assert.Equal(FeasibilityCriterion.SlaFeasibility, result.Criterion);
        Assert.Equal(FeasibilityCriterionStatus.Pass, result.Status);
        Assert.Empty(result.ReasonCodes);
    }

    [Fact]
    public void EstimateEqualToDeadlinePasses()
    {
        var deadline = DateTimeOffset.UnixEpoch.AddMinutes(30);
        var result = SlaFeasibilityCriterion.Evaluate(Evidence(deadline, deadline));

        Assert.Equal(FeasibilityCriterionStatus.Pass, result.Status);
    }

    [Fact]
    public void EstimateAfterDeadlineFails()
    {
        var result = SlaFeasibilityCriterion.Evaluate(Evidence(
            DateTimeOffset.UnixEpoch.AddMinutes(31),
            DateTimeOffset.UnixEpoch.AddMinutes(30)));

        Assert.Equal(FeasibilityCriterionStatus.Fail, result.Status);
        Assert.Equal(new[] { "SLA_DEADLINE_MISSED" }, result.ReasonCodes);
    }

    [Fact]
    public void MissingEstimateRemainsUnresolved()
    {
        var result = SlaFeasibilityCriterion.Evaluate(Evidence(
            null,
            DateTimeOffset.UnixEpoch.AddMinutes(30)));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "SLA_DELIVERY_ESTIMATE_UNAVAILABLE" }, result.ReasonCodes);
    }

    [Fact]
    public void MissingDeadlineRemainsUnresolved()
    {
        var result = SlaFeasibilityCriterion.Evaluate(Evidence(
            DateTimeOffset.UnixEpoch.AddMinutes(20),
            null));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "SLA_DEADLINE_UNAVAILABLE" }, result.ReasonCodes);
    }

    [Fact]
    public void MissingEstimateAndDeadlineReturnBothReasons()
    {
        var result = SlaFeasibilityCriterion.Evaluate(Evidence(null, null));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(
            new[] { "SLA_DELIVERY_ESTIMATE_UNAVAILABLE", "SLA_DEADLINE_UNAVAILABLE" },
            result.ReasonCodes);
    }

    [Fact]
    public void IncompleteEvidenceRemainsUnresolved()
    {
        var evidence = Evidence(
            DateTimeOffset.UnixEpoch.AddMinutes(20),
            DateTimeOffset.UnixEpoch.AddMinutes(30)) with
        {
            Completeness = SlaEvidenceCompleteness.Incomplete,
            ReasonCodes = ["SLA_INPUTS_INCOMPLETE"]
        };

        var result = SlaFeasibilityCriterion.Evaluate(evidence);

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "SLA_INPUTS_INCOMPLETE" }, result.ReasonCodes);
    }

    [Fact]
    public void UnavailableEvidenceRemainsUnresolved()
    {
        var evidence = Evidence(null, null) with
        {
            Completeness = SlaEvidenceCompleteness.Unavailable,
            ReasonCodes = ["SLA_SOURCE_UNAVAILABLE"]
        };

        Assert.Equal(
            FeasibilityCriterionStatus.Unresolved,
            SlaFeasibilityCriterion.Evaluate(evidence).Status);
    }

    [Fact]
    public void MissingEvidenceRemainsUnresolved()
    {
        var result = SlaFeasibilityCriterion.Evaluate(null);

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "SLA_EVIDENCE_UNAVAILABLE" }, result.ReasonCodes);
    }

    [Fact]
    public void CompleteEvidenceCannotCarryIncompleteReasons()
    {
        var evidence = Evidence(
            DateTimeOffset.UnixEpoch.AddMinutes(20),
            DateTimeOffset.UnixEpoch.AddMinutes(30)) with
        {
            ReasonCodes = ["SLA_SOURCE_INCOMPLETE"]
        };

        Assert.Throws<ArgumentException>(() => SlaFeasibilityCriterion.Evaluate(evidence));
    }

    [Fact]
    public void IncompleteEvidenceRequiresReasons()
    {
        var evidence = Evidence(null, null) with
        {
            Completeness = SlaEvidenceCompleteness.Incomplete
        };

        Assert.Throws<ArgumentException>(() => SlaFeasibilityCriterion.Evaluate(evidence));
    }

    [Fact]
    public void InvalidIdentifiersTimestampsAndReasonsAreRejected()
    {
        var invalidId = Evidence(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
            with { EvaluationId = " " };
        var invalidEvaluatedAt = Evidence(DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)
            with { EvaluatedAt = default };
        var invalidEstimate = Evidence(default, DateTimeOffset.UnixEpoch);
        var invalidReason = Evidence(null, null) with
        {
            Completeness = SlaEvidenceCompleteness.Incomplete,
            ReasonCodes = ["sla_missing"]
        };

        Assert.Throws<ArgumentException>(() => SlaFeasibilityCriterion.Evaluate(invalidId));
        Assert.Throws<ArgumentException>(() => SlaFeasibilityCriterion.Evaluate(invalidEvaluatedAt));
        Assert.Throws<ArgumentException>(() => SlaFeasibilityCriterion.Evaluate(invalidEstimate));
        Assert.Throws<ArgumentException>(() => SlaFeasibilityCriterion.Evaluate(invalidReason));
    }

    [Fact]
    public void SlaEvidenceAloneCannotCompleteFeasibilityGate()
    {
        var sla = SlaFeasibilityCriterion.Evaluate(Evidence(
            DateTimeOffset.UnixEpoch.AddMinutes(20),
            DateTimeOffset.UnixEpoch.AddMinutes(30)));
        var gate = FeasibilityEvidenceGate.Evaluate([sla]);

        Assert.Equal(FeasibilityGateStatus.Unresolved, gate.Status);
        Assert.Contains(FeasibilityCriterion.Coverage, gate.MissingCriteria);
    }

    private static SlaFeasibilityEvidence Evidence(
        DateTimeOffset? estimatedDeliveryAt,
        DateTimeOffset? latestAllowedDeliveryAt) =>
        new(
            "evaluation-1",
            DateTimeOffset.UnixEpoch,
            SlaEvidenceCompleteness.Complete,
            estimatedDeliveryAt,
            latestAllowedDeliveryAt,
            []);
}
