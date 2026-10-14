using System.Security.Cryptography;
using System.Text;

namespace Mono.Api.Domain;

public sealed record RoutePlanSnapshot(
    string RouteId,
    string StartLocationId,
    DateTimeOffset StartedAt,
    IReadOnlyList<RouteMissionDeadline> Missions,
    IReadOnlyList<RouteInsertionStop> OrderedStops);

public sealed record RoutePlanVersionWriteCommand(
    string CommandId,
    int ExpectedVersion,
    RoutePlanSnapshot Snapshot);

public enum RoutePlanVersionWriteOutcome
{
    Persisted,
    Replayed,
    Conflict,
    RouteNotFound
}

public sealed record RoutePlanVersionWriteResult(
    RoutePlanVersionWriteOutcome Outcome,
    string RouteId,
    int Version);

public sealed record RoutePlanVersion(int Version, RoutePlanSnapshot Snapshot, DateTimeOffset RecordedAt);

public static class RoutePlanSnapshotValidator
{
    public const int MaximumMissionsPerRoute = RouteInsertionTimeWindowPlanner.MaximumMissionsPerRoute;
    public const int MaximumStopsPerRoute = MaximumMissionsPerRoute * 2;

    public static void Validate(RoutePlanVersionWriteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!IsIdentifier(command.CommandId) || command.ExpectedVersion < 0 || command.Snapshot is null)
            throw new ArgumentException("Route version write command is malformed.", nameof(command));

        var snapshot = command.Snapshot;
        if (!IsIdentifier(snapshot.RouteId) || !IsIdentifier(snapshot.StartLocationId) ||
            !IsDatabaseTimestamp(snapshot.StartedAt) ||
            snapshot.Missions is null || snapshot.OrderedStops is null ||
            snapshot.Missions.Count is < 1 or > MaximumMissionsPerRoute ||
            snapshot.OrderedStops.Count is < 1 or > MaximumStopsPerRoute ||
            snapshot.Missions.Any(mission => mission is null) ||
            snapshot.OrderedStops.Any(stop => stop is null))
        {
            throw new ArgumentException("Route plan snapshot is malformed.", nameof(command));
        }

        var deadlines = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var mission in snapshot.Missions)
        {
            if (!IsIdentifier(mission.MissionId) || !IsDatabaseTimestamp(mission.CommittedDeliveryAt) ||
                !deadlines.TryAdd(mission.MissionId, mission.CommittedDeliveryAt))
            {
                throw new ArgumentException("Route Mission deadlines must be valid and unique.", nameof(command));
            }
        }

        var stopsByMission = new Dictionary<string, List<(RouteInsertionStop Stop, int Index)>>(StringComparer.Ordinal);
        for (var index = 0; index < snapshot.OrderedStops.Count; index++)
        {
            var stop = snapshot.OrderedStops[index];
            if (!Enum.IsDefined(stop.Kind) || !IsIdentifier(stop.MissionId) ||
                !deadlines.ContainsKey(stop.MissionId) || !IsIdentifier(stop.LocationId) ||
                stop.ServiceSeconds < 0 ||
                (stop.Kind == RouteStopKind.Pickup &&
                    (stop.PickupReadyAt is null || !IsDatabaseTimestamp(stop.PickupReadyAt.Value) ||
                     stop.ServiceSeconds > RouteInsertionTimeWindowPlanner.MaximumPickupDwellSeconds)) ||
                (stop.Kind == RouteStopKind.Dropoff && stop.PickupReadyAt is not null))
            {
                throw new ArgumentException("Route stop is malformed.", nameof(command));
            }

            if (!stopsByMission.TryGetValue(stop.MissionId, out var stops))
            {
                stops = [];
                stopsByMission.Add(stop.MissionId, stops);
            }
            stops.Add((stop, index));
        }

        if (stopsByMission.Count != deadlines.Count)
            throw new ArgumentException("Every route Mission must have a stop.", nameof(command));

        foreach (var (missionId, stops) in stopsByMission)
        {
            if (stops.Count is < 1 or > 2 ||
                stops.Count(item => item.Stop.Kind == RouteStopKind.Dropoff) != 1 ||
                stops.Count(item => item.Stop.Kind == RouteStopKind.Pickup) > 1)
            {
                throw new ArgumentException("Each route Mission needs one drop-off and at most one remaining pickup.", nameof(command));
            }

            var pickup = stops.SingleOrDefault(item => item.Stop.Kind == RouteStopKind.Pickup);
            var dropoff = stops.Single(item => item.Stop.Kind == RouteStopKind.Dropoff);
            if (pickup.Stop is not null && pickup.Index > dropoff.Index)
                throw new ArgumentException($"Mission {missionId} pickup must precede its drop-off.", nameof(command));
        }
    }

    public static string ComputeSha256(RoutePlanSnapshot snapshot)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(snapshot.RouteId);
            writer.Write(snapshot.StartLocationId);
            writer.Write(snapshot.StartedAt.UtcDateTime.Ticks);
            writer.Write(snapshot.Missions.Count);
            foreach (var mission in snapshot.Missions.OrderBy(item => item.MissionId, StringComparer.Ordinal))
            {
                writer.Write(mission.MissionId);
                writer.Write(mission.CommittedDeliveryAt.UtcDateTime.Ticks);
            }
            writer.Write(snapshot.OrderedStops.Count);
            foreach (var stop in snapshot.OrderedStops)
            {
                writer.Write(stop.MissionId);
                writer.Write((int)stop.Kind);
                writer.Write(stop.LocationId);
                writer.Write(stop.ServiceSeconds);
                writer.Write(stop.PickupReadyAt?.UtcDateTime.Ticks ?? -1L);
            }
        }

        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static bool IsIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value == value.Trim() &&
        value.Length <= 128 && !value.Any(char.IsControl);

    private static bool IsDatabaseTimestamp(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value.UtcDateTime.Ticks % 10 == 0;
}
