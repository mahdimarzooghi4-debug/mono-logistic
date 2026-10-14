namespace Mono.Api.Domain;

public enum RouteStopKind
{
    Pickup,
    Dropoff
}

public enum RouteInsertionTimeWindowDisposition
{
    CandidatePositionsFound,
    NoCandidatePosition,
    Unresolved
}

public sealed record RouteInsertionMission(
    string MissionId,
    string PickupLocationId,
    string DropoffLocationId,
    DateTimeOffset PickupReadyAt,
    DateTimeOffset CommittedDeliveryAt,
    int PickupServiceSeconds,
    int DropoffServiceSeconds);

public sealed record RouteInsertionStop(
    string MissionId,
    RouteStopKind Kind,
    string LocationId,
    int ServiceSeconds,
    DateTimeOffset? PickupReadyAt = null);

public sealed record RouteTravelTimeMatrix(
    bool IsComplete,
    bool SourceVerified,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int?>> TravelTimeSeconds);

public sealed record RouteInsertionTimeWindowRequest(
    string RouteId,
    int RouteVersion,
    string StartLocationId,
    DateTimeOffset StartedAt,
    IReadOnlyList<RouteInsertionStop> OrderedExistingStops,
    IReadOnlyList<RouteMissionDeadline> ExistingMissions,
    RouteInsertionMission Candidate,
    RouteTravelTimeMatrix TravelMatrix);

public sealed record RouteMissionDeadline(string MissionId, DateTimeOffset CommittedDeliveryAt);

public sealed record RouteInsertionCandidatePosition(
    int PickupIndex,
    int DropoffIndex,
    IReadOnlyDictionary<string, DateTimeOffset> DeliveryTimes);

public sealed record RouteInsertionTimeWindowResult(
    RouteInsertionTimeWindowDisposition Disposition,
    IReadOnlyList<RouteInsertionCandidatePosition> CandidatePositions,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Enumerates pickup/dropoff insertion positions and checks their time windows against an
/// explicit directed travel-time matrix. This is a planning primitive only: it does not
/// verify matrix provenance, capacity, zone authorization, policy, route persistence, or dispatch.
/// </summary>
public static class RouteInsertionTimeWindowPlanner
{
    public const int MaximumMissionsPerRoute = 5;
    public const int MaximumPickupDwellSeconds = 180;

    public static RouteInsertionTimeWindowResult Evaluate(RouteInsertionTimeWindowRequest request)
    {
        ValidateRequest(request);

        if (!request.TravelMatrix.IsComplete || !request.TravelMatrix.SourceVerified)
        {
            var reasons = new List<string>();
            if (!request.TravelMatrix.IsComplete)
                reasons.Add("TRAVEL_MATRIX_INCOMPLETE");
            if (!request.TravelMatrix.SourceVerified)
                reasons.Add("TRAVEL_MATRIX_SOURCE_UNVERIFIED");
            return Unresolved(reasons);
        }

        var missionCount = request.ExistingMissions.Count + 1;
        if (missionCount > MaximumMissionsPerRoute)
        {
            return NoPosition("ROUTE_MISSION_LIMIT_REACHED");
        }

        var locationIds = request.OrderedExistingStops
            .Select(stop => stop.LocationId)
            .Append(request.StartLocationId)
            .Append(request.Candidate.PickupLocationId)
            .Append(request.Candidate.DropoffLocationId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var matrixError = ValidateTravelMatrix(request.TravelMatrix, locationIds);
        if (matrixError is not null)
            return Unresolved([matrixError]);

        var constraints = request.ExistingMissions.ToDictionary(
            mission => mission.MissionId,
            mission => mission.CommittedDeliveryAt,
            StringComparer.Ordinal);
        constraints.Add(request.Candidate.MissionId, request.Candidate.CommittedDeliveryAt);

        var pickup = new RouteInsertionStop(
            request.Candidate.MissionId,
            RouteStopKind.Pickup,
            request.Candidate.PickupLocationId,
            request.Candidate.PickupServiceSeconds,
            request.Candidate.PickupReadyAt);
        var dropoff = new RouteInsertionStop(
            request.Candidate.MissionId,
            RouteStopKind.Dropoff,
            request.Candidate.DropoffLocationId,
            request.Candidate.DropoffServiceSeconds);

        var candidates = new List<RouteInsertionCandidatePosition>();
        var observedReasons = new HashSet<string>(StringComparer.Ordinal);
        var existingStopCount = request.OrderedExistingStops.Count;
        for (var pickupIndex = 0; pickupIndex <= existingStopCount; pickupIndex++)
        {
            for (var dropoffIndex = pickupIndex + 1; dropoffIndex <= existingStopCount + 1; dropoffIndex++)
            {
                var proposedStops = request.OrderedExistingStops.ToList();
                proposedStops.Insert(pickupIndex, pickup);
                proposedStops.Insert(dropoffIndex, dropoff);
                var result = EvaluateSequence(
                    request.StartLocationId,
                    request.StartedAt,
                    proposedStops,
                    constraints,
                    request.TravelMatrix);
                if (result.Feasible)
                {
                    candidates.Add(new RouteInsertionCandidatePosition(
                        pickupIndex,
                        dropoffIndex,
                        result.DeliveryTimes));
                }
                else
                {
                    observedReasons.UnionWith(result.ReasonCodes);
                }
            }
        }

        if (candidates.Count > 0)
        {
            return new RouteInsertionTimeWindowResult(
                RouteInsertionTimeWindowDisposition.CandidatePositionsFound,
                candidates,
                []);
        }

        observedReasons.Add("NO_TIME_WINDOW_FEASIBLE_INSERTION");
        return new RouteInsertionTimeWindowResult(
            RouteInsertionTimeWindowDisposition.NoCandidatePosition,
            [],
            observedReasons.Order(StringComparer.Ordinal).ToArray());
    }

    private static (bool Feasible, IReadOnlyDictionary<string, DateTimeOffset> DeliveryTimes, IReadOnlyList<string> ReasonCodes)
        EvaluateSequence(
            string startLocationId,
            DateTimeOffset startedAt,
            IReadOnlyList<RouteInsertionStop> stops,
            IReadOnlyDictionary<string, DateTimeOffset> committedDeliveryAtByMission,
            RouteTravelTimeMatrix matrix)
    {
        var currentLocationId = startLocationId;
        var currentTime = startedAt;
        var deliveryTimes = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        var reasons = new HashSet<string>(StringComparer.Ordinal);

        foreach (var stop in stops)
        {
            var travelSeconds = StringComparer.Ordinal.Equals(currentLocationId, stop.LocationId)
                ? 0
                : matrix.TravelTimeSeconds[currentLocationId][stop.LocationId];
            if (travelSeconds is null)
                return (false, deliveryTimes, ["TRAVEL_MATRIX_INCOMPLETE"]);

            DateTimeOffset arrival;
            try
            {
                arrival = currentTime.AddSeconds(travelSeconds.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                return (false, deliveryTimes, ["TRAVEL_TIME_OUT_OF_RANGE"]);
            }

            if (stop.Kind == RouteStopKind.Pickup)
            {
                var readyAt = stop.PickupReadyAt!.Value;
                var waitSeconds = readyAt > arrival ? (readyAt - arrival).TotalSeconds : 0;
                if (waitSeconds + stop.ServiceSeconds > MaximumPickupDwellSeconds)
                {
                    reasons.Add("PICKUP_DWELL_LIMIT_EXCEEDED");
                    continue;
                }

                try
                {
                    currentTime = (readyAt > arrival ? readyAt : arrival).AddSeconds(stop.ServiceSeconds);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return (false, deliveryTimes, ["TRAVEL_TIME_OUT_OF_RANGE"]);
                }
            }
            else
            {
                DateTimeOffset completedAt;
                try
                {
                    completedAt = arrival.AddSeconds(stop.ServiceSeconds);
                }
                catch (ArgumentOutOfRangeException)
                {
                    return (false, deliveryTimes, ["TRAVEL_TIME_OUT_OF_RANGE"]);
                }

                if (completedAt > committedDeliveryAtByMission[stop.MissionId])
                {
                    reasons.Add("MISSION_PROMISE_DEADLINE_MISSED");
                    continue;
                }

                deliveryTimes[stop.MissionId] = completedAt;
                currentTime = completedAt;
            }

            currentLocationId = stop.LocationId;
        }

        if (reasons.Count > 0)
            return (false, deliveryTimes, reasons.Order(StringComparer.Ordinal).ToArray());

        return (true, deliveryTimes, []);
    }

    private static string? ValidateTravelMatrix(RouteTravelTimeMatrix matrix, IReadOnlyList<string> locationIds)
    {
        foreach (var origin in locationIds)
        {
            if (!matrix.TravelTimeSeconds.TryGetValue(origin, out var row) || row is null)
                return "TRAVEL_MATRIX_INCOMPLETE";

            foreach (var destination in locationIds)
            {
                if (StringComparer.Ordinal.Equals(origin, destination))
                    continue;
                if (!row.TryGetValue(destination, out var seconds) || seconds is null)
                    return "TRAVEL_MATRIX_INCOMPLETE";
                if (seconds < 0)
                    throw new ArgumentException("Travel durations must not be negative.", nameof(matrix));
            }
        }

        return null;
    }

    private static void ValidateRequest(RouteInsertionTimeWindowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsIdentifier(request.RouteId) || request.RouteVersion <= 0 ||
            !IsIdentifier(request.StartLocationId) || !IsUtc(request.StartedAt) ||
            request.OrderedExistingStops is null || request.ExistingMissions is null ||
            request.Candidate is null || request.TravelMatrix is null ||
            request.TravelMatrix.TravelTimeSeconds is null)
        {
            throw new ArgumentException("Route insertion request is malformed.", nameof(request));
        }

        ValidateMission(request.Candidate, nameof(request));
        var missionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mission in request.ExistingMissions)
        {
            if (mission is null || !IsIdentifier(mission.MissionId) || !IsUtc(mission.CommittedDeliveryAt) ||
                !missionIds.Add(mission.MissionId) ||
                StringComparer.Ordinal.Equals(mission.MissionId, request.Candidate.MissionId))
            {
                throw new ArgumentException("Existing Mission deadlines must be valid and unique.", nameof(request));
            }
        }

        if (request.OrderedExistingStops.Any(stop => stop is null))
            throw new ArgumentException("Existing route stops cannot be null.", nameof(request));

        foreach (var stop in request.OrderedExistingStops)
        {
            if (!IsIdentifier(stop.MissionId) || !missionIds.Contains(stop.MissionId) ||
                !Enum.IsDefined(stop.Kind) || !IsIdentifier(stop.LocationId) ||
                stop.ServiceSeconds < 0 ||
                (stop.Kind == RouteStopKind.Pickup &&
                    (!IsUtc(stop.PickupReadyAt) || stop.ServiceSeconds > MaximumPickupDwellSeconds)) ||
                (stop.Kind == RouteStopKind.Dropoff && stop.PickupReadyAt is not null))
            {
                throw new ArgumentException("Existing route stop is malformed.", nameof(request));
            }
        }

        var groupedStops = request.OrderedExistingStops
            .GroupBy(stop => stop.MissionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (groupedStops.Keys.Any(id => !missionIds.Contains(id)) || groupedStops.Count != missionIds.Count)
        {
            throw new ArgumentException("Every existing Mission must have route stops and every stop must belong to an existing Mission.", nameof(request));
        }

        foreach (var mission in request.ExistingMissions)
        {
            var missionStops = groupedStops[mission.MissionId];
            if (missionStops.Length is < 1 or > 2 ||
                missionStops.Count(stop => stop.Kind == RouteStopKind.Dropoff) != 1 ||
                missionStops.Count(stop => stop.Kind == RouteStopKind.Pickup) > 1)
            {
                throw new ArgumentException("Each active Mission needs one drop-off and at most one remaining pickup stop.", nameof(request));
            }

            var pickupIndex = Array.FindIndex(missionStops, stop => stop.Kind == RouteStopKind.Pickup);
            var dropoffIndex = Array.FindIndex(missionStops, stop => stop.Kind == RouteStopKind.Dropoff);
            if (pickupIndex >= 0 && pickupIndex > dropoffIndex)
                throw new ArgumentException("A Mission pickup must appear before its drop-off.", nameof(request));
        }
    }

    private static void ValidateMission(RouteInsertionMission mission, string parameterName)
    {
        if (mission is null || !IsIdentifier(mission.MissionId) ||
            !IsIdentifier(mission.PickupLocationId) || !IsIdentifier(mission.DropoffLocationId) ||
            !IsUtc(mission.PickupReadyAt) || !IsUtc(mission.CommittedDeliveryAt) ||
            mission.CommittedDeliveryAt <= mission.PickupReadyAt ||
            mission.PickupServiceSeconds is < 0 or > MaximumPickupDwellSeconds ||
            mission.DropoffServiceSeconds < 0)
        {
            throw new ArgumentException("Candidate Mission route constraints are malformed.", parameterName);
        }
    }

    private static bool IsUtc(DateTimeOffset? value) => value is { Offset: var offset } && offset == TimeSpan.Zero;

    private static bool IsUtc(DateTimeOffset value) => value.Offset == TimeSpan.Zero;

    private static bool IsIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);

    private static RouteInsertionTimeWindowResult NoPosition(string reason) =>
        new(RouteInsertionTimeWindowDisposition.NoCandidatePosition, [], [reason]);

    private static RouteInsertionTimeWindowResult Unresolved(IReadOnlyList<string> reasons) =>
        new(RouteInsertionTimeWindowDisposition.Unresolved, [], reasons);
}
