namespace Mono.Api.Domain;

public enum DistanceConstraintStatus
{
    Pass,
    Fail,
    Unresolved
}

/// <summary>
/// Result of evaluating one route-distance constraint. This is not a final Mission decision.
/// </summary>
public sealed record DistanceConstraintResult(
    DistanceConstraintStatus Status,
    double? RouteDistanceMeters,
    double MaximumDistanceMeters,
    string? ReasonCode);

public static class DistanceConstraintEvaluator
{
    public static DistanceConstraintResult Evaluate(
        double? routeDistanceMeters,
        double maximumDistanceMeters)
    {
        if (!double.IsFinite(maximumDistanceMeters) || maximumDistanceMeters < 0)
            throw new ArgumentOutOfRangeException(
                nameof(maximumDistanceMeters),
                "Maximum distance must be a finite non-negative value in metres.");

        if (routeDistanceMeters is null)
            return new DistanceConstraintResult(
                DistanceConstraintStatus.Unresolved,
                null,
                maximumDistanceMeters,
                "ROUTE_DISTANCE_UNAVAILABLE");

        if (!double.IsFinite(routeDistanceMeters.Value) || routeDistanceMeters.Value < 0)
            throw new ArgumentOutOfRangeException(
                nameof(routeDistanceMeters),
                "Route distance must be a finite non-negative value in metres.");

        if (routeDistanceMeters.Value > maximumDistanceMeters)
            return new DistanceConstraintResult(
                DistanceConstraintStatus.Fail,
                routeDistanceMeters.Value,
                maximumDistanceMeters,
                "MAX_DISTANCE_EXCEEDED");

        return new DistanceConstraintResult(
            DistanceConstraintStatus.Pass,
            routeDistanceMeters.Value,
            maximumDistanceMeters,
            null);
    }
}
