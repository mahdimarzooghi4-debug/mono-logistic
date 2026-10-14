using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public enum InitialRoutePlanDisposition
{
    Feasible,
    Infeasible,
    Unresolved
}

public sealed record InitialRoutePlanRequest(
    string RouteId,
    string StartLocationId,
    DateTimeOffset StartedAt,
    RouteInsertionMission Mission,
    RouteTravelTimeMatrix TravelMatrix);

public sealed record InitialRoutePlanEvaluation(
    InitialRoutePlanDisposition Disposition,
    RoutePlanSnapshot? Snapshot,
    DateTimeOffset? EstimatedDeliveryAt,
    IReadOnlyList<string> ReasonCodes);

/// <summary>Evaluates the first Mission on a new Route using directed, verified travel-time evidence.</summary>
public static class InitialRoutePlanEvaluator
{
    private static readonly Regex Identifier = new(@"\A[A-Za-z0-9][A-Za-z0-9._:-]{0,127}\z", RegexOptions.CultureInvariant);

    public static InitialRoutePlanEvaluation Evaluate(InitialRoutePlanRequest request)
    {
        Validate(request);
        if (!request.TravelMatrix.IsComplete || !request.TravelMatrix.SourceVerified)
        {
            var reasons = new List<string>();
            if (!request.TravelMatrix.IsComplete) reasons.Add("TRAVEL_MATRIX_INCOMPLETE");
            if (!request.TravelMatrix.SourceVerified) reasons.Add("TRAVEL_MATRIX_SOURCE_UNVERIFIED");
            return Unresolved(reasons);
        }

        if (!TryDuration(request.StartLocationId, request.Mission.PickupLocationId, out var toPickup) ||
            !TryDuration(request.Mission.PickupLocationId, request.Mission.DropoffLocationId, out var toDropoff))
            return Unresolved(["TRAVEL_MATRIX_INCOMPLETE"]);

        DateTimeOffset arrivalAtPickup;
        DateTimeOffset pickupCompleteAt;
        DateTimeOffset deliveryAt;
        try
        {
            arrivalAtPickup = request.StartedAt.AddSeconds(toPickup);
            var pickupStart = request.Mission.PickupReadyAt > arrivalAtPickup
                ? request.Mission.PickupReadyAt
                : arrivalAtPickup;
            if ((pickupStart - arrivalAtPickup).TotalSeconds + request.Mission.PickupServiceSeconds >
                RouteInsertionTimeWindowPlanner.MaximumPickupDwellSeconds)
                return Infeasible("PICKUP_DWELL_LIMIT_EXCEEDED");
            pickupCompleteAt = pickupStart.AddSeconds(request.Mission.PickupServiceSeconds);
            deliveryAt = pickupCompleteAt.AddSeconds(toDropoff)
                .AddSeconds(request.Mission.DropoffServiceSeconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Unresolved(["TRAVEL_TIME_OUT_OF_RANGE"]);
        }

        if (deliveryAt > request.Mission.CommittedDeliveryAt)
            return Infeasible("MISSION_PROMISE_DEADLINE_MISSED", deliveryAt);

        var snapshot = new RoutePlanSnapshot(
            request.RouteId,
            request.StartLocationId,
            request.StartedAt,
            [new RouteMissionDeadline(request.Mission.MissionId, request.Mission.CommittedDeliveryAt)],
            [
                new RouteInsertionStop(request.Mission.MissionId, RouteStopKind.Pickup,
                    request.Mission.PickupLocationId, request.Mission.PickupServiceSeconds,
                    request.Mission.PickupReadyAt),
                new RouteInsertionStop(request.Mission.MissionId, RouteStopKind.Dropoff,
                    request.Mission.DropoffLocationId, request.Mission.DropoffServiceSeconds)
            ]);
        RoutePlanSnapshotValidator.Validate(new RoutePlanVersionWriteCommand("initial-route-validation", 0, snapshot));
        return new InitialRoutePlanEvaluation(InitialRoutePlanDisposition.Feasible, snapshot, deliveryAt, []);

        bool TryDuration(string origin, string destination, out int seconds)
        {
            if (StringComparer.Ordinal.Equals(origin, destination))
            {
                seconds = 0;
                return true;
            }
            if (request.TravelMatrix.TravelTimeSeconds.TryGetValue(origin, out var row) && row is not null &&
                row.TryGetValue(destination, out var value) && value is >= 0)
            {
                seconds = value.Value;
                return true;
            }
            seconds = 0;
            return false;
        }
    }

    private static void Validate(InitialRoutePlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsIdentifier(request.RouteId) || !IsIdentifier(request.StartLocationId) ||
            request.StartedAt == default || request.StartedAt.Offset != TimeSpan.Zero ||
            request.StartedAt.UtcDateTime.Ticks % 10 != 0 || request.Mission is null ||
            !IsIdentifier(request.Mission.MissionId) || !IsIdentifier(request.Mission.PickupLocationId) ||
            !IsIdentifier(request.Mission.DropoffLocationId) ||
            request.Mission.PickupReadyAt == default || request.Mission.PickupReadyAt.Offset != TimeSpan.Zero ||
            request.Mission.PickupReadyAt.UtcDateTime.Ticks % 10 != 0 ||
            request.Mission.CommittedDeliveryAt == default || request.Mission.CommittedDeliveryAt.Offset != TimeSpan.Zero ||
            request.Mission.CommittedDeliveryAt.UtcDateTime.Ticks % 10 != 0 ||
            request.Mission.CommittedDeliveryAt <= request.Mission.PickupReadyAt ||
            request.Mission.PickupServiceSeconds is < 0 or > RouteInsertionTimeWindowPlanner.MaximumPickupDwellSeconds ||
            request.Mission.DropoffServiceSeconds < 0 || request.TravelMatrix is null ||
            request.TravelMatrix.TravelTimeSeconds is null)
            throw new ArgumentException("Initial route plan request is malformed.", nameof(request));
    }

    private static bool IsIdentifier(string? value) => value is not null && Identifier.IsMatch(value);

    private static InitialRoutePlanEvaluation Infeasible(string reason, DateTimeOffset? deliveryAt = null) =>
        new(InitialRoutePlanDisposition.Infeasible, null, deliveryAt, [reason]);

    private static InitialRoutePlanEvaluation Unresolved(IReadOnlyList<string> reasons) =>
        new(InitialRoutePlanDisposition.Unresolved, null, null, reasons);
}
