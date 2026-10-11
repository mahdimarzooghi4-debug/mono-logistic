using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class ActiveRouteInventoryCriterionTests
{
    [Fact]
    public void CompleteEmptyInventoryPassesAsKnownEvidence()
    {
        var result = ActiveRouteInventoryCriterion.Evaluate(Snapshot(
            RouteInventoryCompleteness.Complete, []));

        Assert.Equal(FeasibilityCriterion.ActiveRoutes, result.Criterion);
        Assert.Equal(FeasibilityCriterionStatus.Pass, result.Status);
        Assert.Empty(result.ReasonCodes);
    }

    [Fact]
    public void CompleteInventoryWithKnownRouteActivityPasses()
    {
        var result = ActiveRouteInventoryCriterion.Evaluate(Snapshot(
            RouteInventoryCompleteness.Complete,
            [
                Route("route-1", RouteInventoryActivity.Active),
                Route("route-2", RouteInventoryActivity.Inactive)
            ]));

        Assert.Equal(FeasibilityCriterionStatus.Pass, result.Status);
    }

    [Fact]
    public void IncompleteInventoryRemainsUnresolved()
    {
        var result = ActiveRouteInventoryCriterion.Evaluate(Snapshot(
            RouteInventoryCompleteness.Incomplete,
            [Route("route-1", RouteInventoryActivity.Active)],
            ["ROUTE_SCAN_INCOMPLETE"]));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "ROUTE_SCAN_INCOMPLETE" }, result.ReasonCodes);
    }

    [Fact]
    public void UnknownRouteActivityRemainsUnresolvedEvenInCompleteSnapshot()
    {
        var result = ActiveRouteInventoryCriterion.Evaluate(Snapshot(
            RouteInventoryCompleteness.Complete,
            [Route("route-1", RouteInventoryActivity.Unresolved, "ROUTE_STATE_UNKNOWN")]));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "ROUTE_STATE_UNKNOWN" }, result.ReasonCodes);
    }

    [Fact]
    public void UnavailableInventoryRemainsUnresolved()
    {
        var result = ActiveRouteInventoryCriterion.Evaluate(Snapshot(
            RouteInventoryCompleteness.Unavailable,
            [],
            ["ROUTE_SOURCE_UNAVAILABLE"]));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "ROUTE_SOURCE_UNAVAILABLE" }, result.ReasonCodes);
    }

    [Fact]
    public void MissingSnapshotRemainsUnresolved()
    {
        var result = ActiveRouteInventoryCriterion.Evaluate(null);

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "ROUTE_INVENTORY_UNAVAILABLE" }, result.ReasonCodes);
    }

    [Fact]
    public void DuplicateRouteIdsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => ActiveRouteInventoryCriterion.Evaluate(Snapshot(
            RouteInventoryCompleteness.Complete,
            [
                Route("route-1", RouteInventoryActivity.Active),
                Route("route-1", RouteInventoryActivity.Inactive)
            ])));
    }

    [Fact]
    public void IncompleteInventoryRequiresReasonCode()
    {
        Assert.Throws<ArgumentException>(() => ActiveRouteInventoryCriterion.Evaluate(Snapshot(
            RouteInventoryCompleteness.Incomplete, [])));
    }

    [Fact]
    public void UnavailableInventoryCannotContainRouteEntries()
    {
        Assert.Throws<ArgumentException>(() => ActiveRouteInventoryCriterion.Evaluate(Snapshot(
            RouteInventoryCompleteness.Unavailable,
            [Route("route-1", RouteInventoryActivity.Active)],
            ["ROUTE_SOURCE_UNAVAILABLE"])));
    }

    [Fact]
    public void CompleteInventoryCannotClaimSourceIncompleteness()
    {
        Assert.Throws<ArgumentException>(() => ActiveRouteInventoryCriterion.Evaluate(Snapshot(
            RouteInventoryCompleteness.Complete, [], ["ROUTE_SCAN_INCOMPLETE"])));
    }

    [Fact]
    public void NonCanonicalReasonsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => ActiveRouteInventoryCriterion.Evaluate(Snapshot(
            RouteInventoryCompleteness.Incomplete, [], ["route_state_unknown"])));
    }

    [Fact]
    public void ActiveRouteEvidenceAloneCannotCompleteFeasibilityGate()
    {
        var routes = ActiveRouteInventoryCriterion.Evaluate(Snapshot(
            RouteInventoryCompleteness.Complete, []));
        var gate = FeasibilityEvidenceGate.Evaluate([routes]);

        Assert.Equal(FeasibilityGateStatus.Unresolved, gate.Status);
        Assert.Contains(FeasibilityCriterion.Coverage, gate.MissingCriteria);
    }

    private static RouteInventorySnapshot Snapshot(
        RouteInventoryCompleteness completeness,
        IReadOnlyCollection<RouteInventoryEntry> routes,
        IReadOnlyList<string>? reasonCodes = null) =>
        new("zone-1", DateTimeOffset.UnixEpoch, completeness, routes, reasonCodes ?? []);

    private static RouteInventoryEntry Route(
        string routeId,
        RouteInventoryActivity activity,
        params string[] reasonCodes) =>
        new(routeId, activity, reasonCodes);
}
