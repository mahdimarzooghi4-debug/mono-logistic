using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class FeasibilityEvidenceGateTests
{
    [Fact]
    public void All_eight_passed_criteria_allow_outcome_evaluation()
    {
        var result = FeasibilityEvidenceGate.Evaluate(AllPassed());

        Assert.Equal(FeasibilityGateStatus.ReadyForOutcomeEvaluation, result.Status);
        Assert.Equal(8, result.Criteria.Count);
        Assert.Empty(result.MissingCriteria);
    }

    [Fact]
    public void Missing_criteria_leave_the_gate_unresolved()
    {
        var result = FeasibilityEvidenceGate.Evaluate(AllPassed()
            .Where(item => item.Criterion != FeasibilityCriterion.RouteInsertion).ToArray());

        Assert.Equal(FeasibilityGateStatus.Unresolved, result.Status);
        Assert.Contains(FeasibilityCriterion.RouteInsertion, result.MissingCriteria);
        Assert.Contains("FEASIBILITY_EVIDENCE_INCOMPLETE", result.ReasonCodes);
    }

    [Fact]
    public void Unresolved_criterion_leaves_the_gate_unresolved()
    {
        var results = AllPassed().Select(item => item.Criterion == FeasibilityCriterion.Capacity
            ? item with { Status = FeasibilityCriterionStatus.Unresolved, ReasonCodes = ["CAPACITY_DATA_MISSING"] }
            : item).ToArray();

        var result = FeasibilityEvidenceGate.Evaluate(results);

        Assert.Equal(FeasibilityGateStatus.Unresolved, result.Status);
        Assert.Contains("CAPACITY_DATA_MISSING", result.ReasonCodes);
    }

    [Fact]
    public void Failed_criterion_blocks_outcome_evaluation()
    {
        var results = AllPassed().Select(item => item.Criterion == FeasibilityCriterion.Distance
            ? item with { Status = FeasibilityCriterionStatus.Fail, ReasonCodes = ["MAX_DISTANCE_EXCEEDED"] }
            : item).ToArray();

        var result = FeasibilityEvidenceGate.Evaluate(results);

        Assert.Equal(FeasibilityGateStatus.Blocked, result.Status);
        Assert.Contains("MAX_DISTANCE_EXCEEDED", result.ReasonCodes);
        Assert.Contains("FEASIBILITY_CRITERION_FAILED", result.ReasonCodes);
    }

    [Fact]
    public void Known_failure_blocks_even_when_other_criteria_are_missing()
    {
        var results = AllPassed().Where(item => item.Criterion != FeasibilityCriterion.SlaFeasibility)
            .Select(item => item.Criterion == FeasibilityCriterion.Distance
                ? item with { Status = FeasibilityCriterionStatus.Fail, ReasonCodes = ["MAX_DISTANCE_EXCEEDED"] }
                : item).ToArray();

        var result = FeasibilityEvidenceGate.Evaluate(results);

        Assert.Equal(FeasibilityGateStatus.Blocked, result.Status);
        Assert.Contains(FeasibilityCriterion.SlaFeasibility, result.MissingCriteria);
    }

    [Fact]
    public void Results_are_returned_in_canonical_order()
    {
        var result = FeasibilityEvidenceGate.Evaluate(AllPassed().Reverse().ToArray());

        Assert.Equal(FeasibilityEvidenceGate.Required, result.Criteria.Select(item => item.Criterion));
    }

    [Fact]
    public void Duplicate_criteria_are_rejected()
    {
        var results = AllPassed().Append(AllPassed()[0]).ToArray();

        Assert.Throws<ArgumentException>(() => FeasibilityEvidenceGate.Evaluate(results));
    }

    [Fact]
    public void Non_pass_results_require_reason_evidence()
    {
        var results = AllPassed().Select(item => item.Criterion == FeasibilityCriterion.Distance
            ? item with { Status = FeasibilityCriterionStatus.Unresolved, ReasonCodes = [] }
            : item).ToArray();

        Assert.Throws<ArgumentException>(() => FeasibilityEvidenceGate.Evaluate(results));
    }

    [Fact]
    public void Rejects_non_canonical_reason_codes()
    {
        var results = AllPassed().Select(item => item.Criterion == FeasibilityCriterion.Distance
            ? item with { ReasonCodes = ["max-distance-exceeded"] }
            : item).ToArray();

        Assert.Throws<ArgumentException>(() => FeasibilityEvidenceGate.Evaluate(results));
    }

    [Fact]
    public void Null_input_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => FeasibilityEvidenceGate.Evaluate(null!));
    }

    private static FeasibilityCriterionResult[] AllPassed() =>
        FeasibilityEvidenceGate.Required.Select(criterion => new FeasibilityCriterionResult(
            criterion, FeasibilityCriterionStatus.Pass, [])).ToArray();
}
