using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class RoutePlanningFeasibilityGateTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Composes_all_eight_criteria_when_zone_and_route_evidence_is_complete()
    {
        var result = RoutePlanningFeasibilityGate.Evaluate(Request());

        Assert.Equal(FeasibilityGateStatus.ReadyForOutcomeEvaluation, result.Status);
        Assert.Equal(8, result.Criteria.Count);
        Assert.Empty(result.MissingCriteria);
        Assert.Equal(FeasibilityEvidenceGate.Required, result.Criteria.Select(item => item.Criterion));
    }

    [Fact]
    public void Does_not_combine_capacity_or_inventory_from_a_different_zone()
    {
        var request = Request() with
        {
            Capacity = Capacity("zone-2"),
            ActiveRoutes = Inventory("zone-3")
        };

        var result = RoutePlanningFeasibilityGate.Evaluate(request);

        Assert.Equal(FeasibilityGateStatus.Unresolved, result.Status);
        Assert.Contains("ZONE_CAPACITY_ZONE_MISMATCH", result.ReasonCodes);
        Assert.Contains("ROUTE_INVENTORY_ZONE_MISMATCH", result.ReasonCodes);
    }

    [Fact]
    public void Known_capacity_failure_blocks_even_when_other_criteria_are_missing()
    {
        var result = RoutePlanningFeasibilityGate.Evaluate(Request() with
        {
            OtherCriteria = [],
            Capacity = Capacity("zone-1", effective: 0)
        });

        Assert.Equal(FeasibilityGateStatus.Blocked, result.Status);
        Assert.Contains("EFFECTIVE_CAPACITY_INSUFFICIENT", result.ReasonCodes);
        Assert.Contains(FeasibilityCriterion.Coverage, result.MissingCriteria);
    }

    [Fact]
    public void Missing_zone_context_keeps_capacity_and_inventory_unresolved()
    {
        var result = RoutePlanningFeasibilityGate.Evaluate(Request() with { ExpectedZoneId = null });

        Assert.Equal(FeasibilityGateStatus.Unresolved, result.Status);
        Assert.Equal(2, result.Criteria.Count(item =>
            item.Status == FeasibilityCriterionStatus.Unresolved &&
            item.ReasonCodes.Contains("PLANNING_ZONE_UNAVAILABLE")));
    }

    [Fact]
    public void Route_insertion_evidence_must_bind_to_the_expected_zone()
    {
        var result = RoutePlanningFeasibilityGate.Evaluate(Request() with
        {
            RouteInsertion = new RouteInsertionEvidence(
                "evaluation-1", ObservedAt, RouteInsertionEvidenceCompleteness.Complete,
                RouteInsertionFeasibility.Feasible, [], "zone-2")
        });

        Assert.Equal(FeasibilityGateStatus.Unresolved, result.Status);
        Assert.Contains("ROUTE_INSERTION_ZONE_MISMATCH", result.ReasonCodes);
    }

    [Fact]
    public void Unknown_zone_and_missing_zone_registry_remain_unresolved()
    {
        var unknown = RoutePlanningFeasibilityGate.Evaluate(Request() with { ExpectedZoneId = "unknown-zone" });
        Assert.Contains("PLANNING_ZONE_UNKNOWN", unknown.ReasonCodes);

        var unavailable = RoutePlanningFeasibilityGate.Evaluate(Request() with { ZoneRegistry = null });
        Assert.Contains("ZONE_REGISTRY_UNAVAILABLE", unavailable.ReasonCodes);
    }

    [Fact]
    public void Missing_and_incomplete_route_evidence_never_passes_the_gate()
    {
        var result = RoutePlanningFeasibilityGate.Evaluate(Request() with
        {
            Capacity = null,
            RequiredEffectiveCourierCount = null,
            ActiveRoutes = null,
            RouteInsertion = null
        });

        Assert.Equal(FeasibilityGateStatus.Unresolved, result.Status);
        Assert.Contains("ZONE_CAPACITY_SNAPSHOT_MISSING", result.ReasonCodes);
        Assert.Contains("ROUTE_INVENTORY_UNAVAILABLE", result.ReasonCodes);
        Assert.Contains("ROUTE_INSERTION_EVIDENCE_UNAVAILABLE", result.ReasonCodes);
    }

    [Fact]
    public void Rejects_callers_that_supply_duplicate_or_route_owned_criteria()
    {
        var request = Request() with
        {
            OtherCriteria = [Pass(FeasibilityCriterion.Coverage), Pass(FeasibilityCriterion.Capacity)]
        };

        Assert.Throws<ArgumentException>(() => RoutePlanningFeasibilityGate.Evaluate(request));
    }

    [Fact]
    public void Preserves_unresolved_route_insertion_result()
    {
        var request = Request() with
        {
            RouteInsertion = new RouteInsertionEvidence(
                "evaluation-1", ObservedAt, RouteInsertionEvidenceCompleteness.Incomplete,
                RouteInsertionFeasibility.Unresolved, ["ROUTE_MATRIX_INCOMPLETE"], "zone-1")
        };

        var result = RoutePlanningFeasibilityGate.Evaluate(request);

        Assert.Equal(FeasibilityGateStatus.Unresolved, result.Status);
        Assert.Contains("ROUTE_MATRIX_INCOMPLETE", result.ReasonCodes);
    }

    private static RoutePlanningFeasibilityRequest Request() => new(
        "zone-1",
        ZoneRegistry("zone-1"),
        [
            Pass(FeasibilityCriterion.Coverage),
            Pass(FeasibilityCriterion.Distance),
            Pass(FeasibilityCriterion.EligibleModes),
            Pass(FeasibilityCriterion.EligibleCouriers),
            Pass(FeasibilityCriterion.SlaFeasibility)
        ],
        Capacity("zone-1"),
        1,
        Inventory("zone-1"),
        new RouteInsertionEvidence(
            "evaluation-1", ObservedAt, RouteInsertionEvidenceCompleteness.Complete,
            RouteInsertionFeasibility.Feasible, [], "zone-1"));

    private static IReadOnlyCollection<ZoneDefinition> ZoneRegistry(string zoneId) =>
        [new ZoneDefinition { ZoneId = zoneId, Name = "Test Zone" }];

    private static ZoneCapacitySnapshot Capacity(string zoneId, int effective = 1) => new()
    {
        ZoneId = zoneId,
        ObservedAt = ObservedAt,
        RegisteredCount = effective,
        OnlineCount = effective,
        EffectiveCount = effective
    };

    private static RouteInventorySnapshot Inventory(string zoneId) => new(
        zoneId, ObservedAt, RouteInventoryCompleteness.Complete, [], []);

    private static FeasibilityCriterionResult Pass(FeasibilityCriterion criterion) =>
        new(criterion, FeasibilityCriterionStatus.Pass, []);
}
