using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public enum RouteInventoryCompleteness
{
    Complete,
    Incomplete,
    Unavailable
}

public enum RouteInventoryActivity
{
    Active,
    Inactive,
    Unresolved
}

public sealed record RouteInventoryEntry(
    string RouteId,
    RouteInventoryActivity Activity,
    IReadOnlyList<string> ReasonCodes);

public sealed record RouteInventorySnapshot(
    string ZoneId,
    DateTimeOffset ObservedAt,
    RouteInventoryCompleteness Completeness,
    IReadOnlyCollection<RouteInventoryEntry> Routes,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Verifies whether the active-route inventory for a Zone is complete enough for later
/// route-insertion evaluation. An empty complete inventory is valid evidence.
/// </summary>
public static class ActiveRouteInventoryCriterion
{
    private static readonly Regex ReasonCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static FeasibilityCriterionResult Evaluate(RouteInventorySnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return new FeasibilityCriterionResult(
                FeasibilityCriterion.ActiveRoutes,
                FeasibilityCriterionStatus.Unresolved,
                ["ROUTE_INVENTORY_UNAVAILABLE"]);
        }

        if (string.IsNullOrWhiteSpace(snapshot.ZoneId) ||
            snapshot.ZoneId.Length > 128 ||
            snapshot.ZoneId.Any(char.IsControl) ||
            snapshot.ObservedAt == default ||
            !Enum.IsDefined(snapshot.Completeness) ||
            snapshot.Routes is null ||
            snapshot.ReasonCodes is null)
        {
            throw new ArgumentException("Route inventory snapshot is malformed.", nameof(snapshot));
        }

        ValidateReasonCodes(snapshot.ReasonCodes, nameof(snapshot));
        if (snapshot.Completeness == RouteInventoryCompleteness.Complete &&
            snapshot.ReasonCodes.Count > 0)
        {
            throw new ArgumentException("A complete inventory cannot carry incomplete-source reasons.", nameof(snapshot));
        }
        if (snapshot.Completeness != RouteInventoryCompleteness.Complete &&
            snapshot.ReasonCodes.Count == 0)
        {
            throw new ArgumentException("Incomplete or unavailable inventories require reason codes.", nameof(snapshot));
        }
        if (snapshot.Completeness == RouteInventoryCompleteness.Unavailable &&
            snapshot.Routes.Count != 0)
        {
            throw new ArgumentException("An unavailable inventory cannot include route entries.", nameof(snapshot));
        }

        var routeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var route in snapshot.Routes)
        {
            if (route is null ||
                string.IsNullOrWhiteSpace(route.RouteId) ||
                route.RouteId.Length > 128 ||
                route.RouteId.Any(char.IsControl) ||
                !routeIds.Add(route.RouteId) ||
                !Enum.IsDefined(route.Activity) ||
                route.ReasonCodes is null)
            {
                throw new ArgumentException("Route inventory entries must be valid and unique.", nameof(snapshot));
            }

            ValidateReasonCodes(route.ReasonCodes, nameof(snapshot));
            if (route.Activity == RouteInventoryActivity.Unresolved && route.ReasonCodes.Count == 0)
                throw new ArgumentException("Unresolved routes require reason codes.", nameof(snapshot));
            if (route.Activity != RouteInventoryActivity.Unresolved && route.ReasonCodes.Count > 0)
                throw new ArgumentException("Known route activity cannot carry unresolved reasons.", nameof(snapshot));
        }

        var unresolvedRoutes = snapshot.Routes
            .Where(route => route.Activity == RouteInventoryActivity.Unresolved)
            .ToArray();
        if (snapshot.Completeness == RouteInventoryCompleteness.Complete && unresolvedRoutes.Length == 0)
        {
            return new FeasibilityCriterionResult(
                FeasibilityCriterion.ActiveRoutes,
                FeasibilityCriterionStatus.Pass,
                []);
        }

        var reasons = snapshot.ReasonCodes
            .Concat(unresolvedRoutes.SelectMany(route => route.ReasonCodes))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new FeasibilityCriterionResult(
            FeasibilityCriterion.ActiveRoutes,
            FeasibilityCriterionStatus.Unresolved,
            reasons.Length > 0 ? reasons : ["ROUTE_INVENTORY_INCOMPLETE"]);
    }

    private static void ValidateReasonCodes(IReadOnlyList<string> reasonCodes, string parameterName)
    {
        if (reasonCodes.Any(code => string.IsNullOrWhiteSpace(code) || !ReasonCodePattern.IsMatch(code)) ||
            reasonCodes.Distinct(StringComparer.Ordinal).Count() != reasonCodes.Count)
        {
            throw new ArgumentException("Reason codes must be canonical uppercase identifiers without duplicates.", parameterName);
        }
    }
}
