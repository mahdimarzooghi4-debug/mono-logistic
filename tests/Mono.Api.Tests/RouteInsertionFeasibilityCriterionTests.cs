using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class RouteInsertionFeasibilityCriterionTests
{
    [Fact]
    public void CompleteFeasibleEvidencePasses()
    {
        var result = RouteInsertionFeasibilityCriterion.Evaluate(Evidence(
            RouteInsertionEvidenceCompleteness.Complete,
            RouteInsertionFeasibility.Feasible));

        Assert.Equal(FeasibilityCriterion.RouteInsertion, result.Criterion);
        Assert.Equal(FeasibilityCriterionStatus.Pass, result.Status);
        Assert.Empty(result.ReasonCodes);
    }

    [Fact]
    public void CompleteInfeasibleEvidenceFailsWithStableAndSourceReasons()
    {
        var result = RouteInsertionFeasibilityCriterion.Evaluate(Evidence(
            RouteInsertionEvidenceCompleteness.Complete,
            RouteInsertionFeasibility.Infeasible,
            "NO_VALID_INSERTION_POSITION"));

        Assert.Equal(FeasibilityCriterionStatus.Fail, result.Status);
        Assert.Equal(
            new[] { "ROUTE_INSERTION_NOT_FEASIBLE", "NO_VALID_INSERTION_POSITION" },
            result.ReasonCodes);
    }

    [Fact]
    public void IncompleteEvidenceRemainsUnresolved()
    {
        var result = RouteInsertionFeasibilityCriterion.Evaluate(Evidence(
            RouteInsertionEvidenceCompleteness.Incomplete,
            RouteInsertionFeasibility.Unresolved,
            "ROUTE_SIMULATION_INCOMPLETE"));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "ROUTE_SIMULATION_INCOMPLETE" }, result.ReasonCodes);
    }

    [Fact]
    public void UnavailableEvidenceRemainsUnresolved()
    {
        var result = RouteInsertionFeasibilityCriterion.Evaluate(Evidence(
            RouteInsertionEvidenceCompleteness.Unavailable,
            RouteInsertionFeasibility.Unresolved,
            "ROUTE_SIMULATOR_UNAVAILABLE"));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
    }

    [Fact]
    public void MissingEvidenceRemainsUnresolved()
    {
        var result = RouteInsertionFeasibilityCriterion.Evaluate(null);

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "ROUTE_INSERTION_EVIDENCE_UNAVAILABLE" }, result.ReasonCodes);
    }

    [Fact]
    public void CompleteFeasibleEvidenceCannotCarryFailureReasons()
    {
        Assert.Throws<ArgumentException>(() => RouteInsertionFeasibilityCriterion.Evaluate(Evidence(
            RouteInsertionEvidenceCompleteness.Complete,
            RouteInsertionFeasibility.Feasible,
            "ROUTE_SIMULATION_WARNING")));
    }

    [Fact]
    public void CompleteInfeasibleEvidenceRequiresReasons()
    {
        Assert.Throws<ArgumentException>(() => RouteInsertionFeasibilityCriterion.Evaluate(Evidence(
            RouteInsertionEvidenceCompleteness.Complete,
            RouteInsertionFeasibility.Infeasible)));
    }

    [Fact]
    public void IncompleteEvidenceCannotClaimDefinitiveFeasibility()
    {
        Assert.Throws<ArgumentException>(() => RouteInsertionFeasibilityCriterion.Evaluate(Evidence(
            RouteInsertionEvidenceCompleteness.Incomplete,
            RouteInsertionFeasibility.Feasible)));
    }

    [Fact]
    public void CompleteEvidenceCannotBeUnresolved()
    {
        Assert.Throws<ArgumentException>(() => RouteInsertionFeasibilityCriterion.Evaluate(Evidence(
            RouteInsertionEvidenceCompleteness.Complete,
            RouteInsertionFeasibility.Unresolved,
            "ROUTE_RESULT_UNKNOWN")));
    }

    [Fact]
    public void EvidenceRequiresIdentifierAndObservationTime()
    {
        var missingId = Evidence(RouteInsertionEvidenceCompleteness.Complete, RouteInsertionFeasibility.Feasible)
            with { EvaluationId = " " };
        var missingTime = Evidence(RouteInsertionEvidenceCompleteness.Complete, RouteInsertionFeasibility.Feasible)
            with { EvaluatedAt = default };

        Assert.Throws<ArgumentException>(() => RouteInsertionFeasibilityCriterion.Evaluate(missingId));
        Assert.Throws<ArgumentException>(() => RouteInsertionFeasibilityCriterion.Evaluate(missingTime));
    }

    [Fact]
    public void NonCanonicalAndDuplicateReasonsAreRejected()
    {
        var invalid = Evidence(
            RouteInsertionEvidenceCompleteness.Incomplete,
            RouteInsertionFeasibility.Unresolved,
            "route_simulation_incomplete");
        var duplicate = Evidence(
            RouteInsertionEvidenceCompleteness.Incomplete,
            RouteInsertionFeasibility.Unresolved,
            "ROUTE_SIMULATION_INCOMPLETE",
            "ROUTE_SIMULATION_INCOMPLETE");

        Assert.Throws<ArgumentException>(() => RouteInsertionFeasibilityCriterion.Evaluate(invalid));
        Assert.Throws<ArgumentException>(() => RouteInsertionFeasibilityCriterion.Evaluate(duplicate));
    }

    [Fact]
    public void RouteInsertionEvidenceAloneCannotCompleteFeasibilityGate()
    {
        var routeInsertion = RouteInsertionFeasibilityCriterion.Evaluate(Evidence(
            RouteInsertionEvidenceCompleteness.Complete,
            RouteInsertionFeasibility.Feasible));
        var gate = FeasibilityEvidenceGate.Evaluate([routeInsertion]);

        Assert.Equal(FeasibilityGateStatus.Unresolved, gate.Status);
        Assert.Contains(FeasibilityCriterion.ActiveRoutes, gate.MissingCriteria);
    }

    private static RouteInsertionEvidence Evidence(
        RouteInsertionEvidenceCompleteness completeness,
        RouteInsertionFeasibility feasibility,
        params string[] reasonCodes) =>
        new("evaluation-1", DateTimeOffset.UnixEpoch, completeness, feasibility, reasonCodes);
}
