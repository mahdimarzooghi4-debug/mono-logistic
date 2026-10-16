namespace Mono.Api.Domain;

/// <summary>
/// Request inputs for composing the route-related E7.1 criteria with the remaining
/// criterion results. The expected Zone is explicit so capacity and route inventory
/// snapshots from different Zones cannot be combined into one gate result.
/// </summary>
public sealed record RoutePlanningFeasibilityRequest(
    string? ExpectedZoneId,
    IReadOnlyCollection<ZoneDefinition>? ZoneRegistry,
    IReadOnlyCollection<FeasibilityCriterionResult> OtherCriteria,
    ZoneCapacitySnapshot? Capacity,
    int? RequiredEffectiveCourierCount,
    RouteInventorySnapshot? ActiveRoutes,
    RouteInsertionEvidence? RouteInsertion);

/// <summary>
/// Builds the full E7.1 evidence gate from caller-supplied non-route results and
/// the route planning, effective-capacity, and active-route evidence contracts.
/// This composes evidence; it does not establish source trust or freshness.
/// </summary>
public static class RoutePlanningFeasibilityGate
{
    private static readonly HashSet<FeasibilityCriterion> RouteCriteria =
    [
        FeasibilityCriterion.Capacity,
        FeasibilityCriterion.ActiveRoutes,
        FeasibilityCriterion.RouteInsertion
    ];

    public static FeasibilityGateResult Evaluate(RoutePlanningFeasibilityRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.OtherCriteria);

        if (request.OtherCriteria.Any(result => result is null || RouteCriteria.Contains(result.Criterion)))
            throw new ArgumentException(
                "Other criteria cannot contain route-owned results or null entries.", nameof(request));

        var (expectedZoneId, zoneContextFailure) = ResolveZoneContext(
            request.ExpectedZoneId, request.ZoneRegistry);
        var capacity = EvaluateCapacity(request, expectedZoneId, zoneContextFailure);
        var activeRoutes = EvaluateActiveRoutes(request.ActiveRoutes, expectedZoneId, zoneContextFailure);
        var insertion = EvaluateRouteInsertion(request.RouteInsertion, expectedZoneId, zoneContextFailure);

        return FeasibilityEvidenceGate.Evaluate(request.OtherCriteria
            .Concat([capacity, activeRoutes, insertion])
            .ToArray());
    }

    private static FeasibilityCriterionResult EvaluateCapacity(
        RoutePlanningFeasibilityRequest request,
        string? expectedZoneId,
        string? zoneContextFailure)
    {
        if (expectedZoneId is null)
            return Unresolved(FeasibilityCriterion.Capacity, zoneContextFailure!);
        if (request.Capacity is not null)
        {
            var errors = ZoneCapacitySnapshotValidator.Validate(request.Capacity, request.ZoneRegistry!);
            if (errors.Count > 0)
                throw new ArgumentException("Zone capacity snapshot is invalid: " + string.Join("; ", errors), nameof(request));
            if (!StringComparer.Ordinal.Equals(request.Capacity.Normalize().ZoneId, expectedZoneId))
                return Unresolved(FeasibilityCriterion.Capacity, "ZONE_CAPACITY_ZONE_MISMATCH");
        }

        return EffectiveZoneCapacityCriterion.Evaluate(
            request.Capacity, request.RequiredEffectiveCourierCount);
    }

    private static FeasibilityCriterionResult EvaluateRouteInsertion(
        RouteInsertionEvidence? evidence,
        string? expectedZoneId,
        string? zoneContextFailure)
    {
        if (evidence is null)
            return RouteInsertionFeasibilityCriterion.Evaluate(null);
        var result = RouteInsertionFeasibilityCriterion.Evaluate(evidence);
        if (expectedZoneId is null)
            return Unresolved(FeasibilityCriterion.RouteInsertion, zoneContextFailure!);
        if (evidence.ZoneId is null)
            return Unresolved(FeasibilityCriterion.RouteInsertion, "ROUTE_INSERTION_ZONE_UNAVAILABLE");
        if (!StringComparer.Ordinal.Equals(evidence.ZoneId.Trim(), expectedZoneId))
            return Unresolved(FeasibilityCriterion.RouteInsertion, "ROUTE_INSERTION_ZONE_MISMATCH");

        return result;
    }

    private static FeasibilityCriterionResult EvaluateActiveRoutes(
        RouteInventorySnapshot? snapshot,
        string? expectedZoneId,
        string? zoneContextFailure)
    {
        if (expectedZoneId is null)
            return Unresolved(FeasibilityCriterion.ActiveRoutes, zoneContextFailure!);
        if (snapshot is null)
            return ActiveRouteInventoryCriterion.Evaluate(null);
        var result = ActiveRouteInventoryCriterion.Evaluate(snapshot);
        if (!StringComparer.Ordinal.Equals(snapshot.ZoneId, expectedZoneId))
            return Unresolved(FeasibilityCriterion.ActiveRoutes, "ROUTE_INVENTORY_ZONE_MISMATCH");

        return result;
    }

    private static (string? ZoneId, string? FailureReason) ResolveZoneContext(
        string? zoneId,
        IReadOnlyCollection<ZoneDefinition>? zoneRegistry)
    {
        if (zoneRegistry is null)
            return (null, "ZONE_REGISTRY_UNAVAILABLE");

        var registryErrors = ZoneHierarchyValidator.Validate(zoneRegistry);
        if (registryErrors.Count > 0)
            throw new ArgumentException("Zone registry is invalid: " + string.Join("; ", registryErrors), nameof(zoneRegistry));

        if (string.IsNullOrWhiteSpace(zoneId) || zoneId.Length > 128 || zoneId.Any(char.IsControl))
            return (null, "PLANNING_ZONE_UNAVAILABLE");

        var normalized = zoneId.Trim();
        var knownZoneIds = zoneRegistry.Select(zone => zone.Normalize().ZoneId)
            .ToHashSet(StringComparer.Ordinal);
        return knownZoneIds.Contains(normalized)
            ? (normalized, null)
            : (null, "PLANNING_ZONE_UNKNOWN");
    }

    private static FeasibilityCriterionResult Unresolved(
        FeasibilityCriterion criterion,
        string reasonCode) =>
        new(criterion, FeasibilityCriterionStatus.Unresolved, [reasonCode]);
}
