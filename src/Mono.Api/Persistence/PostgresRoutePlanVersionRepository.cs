using System.Security.Cryptography;
using Mono.Api.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>
/// Persists immutable planning snapshots with optimistic route-version checks.
/// This repository does not run simulation or authorize dispatch.
/// </summary>
public sealed class PostgresRoutePlanVersionRepository : IRoutePlanVersionStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeProvider _timeProvider;

    public PostgresRoutePlanVersionRepository(NpgsqlDataSource dataSource, TimeProvider? timeProvider = null)
    {
        _dataSource = dataSource;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<RoutePlanVersionWriteResult> PersistAsync(
        RoutePlanVersionWriteCommand command,
        CancellationToken cancellationToken = default)
    {
        RoutePlanSnapshotValidator.Validate(command);
        var snapshot = command.Snapshot;
        var snapshotHash = RoutePlanSnapshotValidator.ComputeSha256(snapshot);
        var recordedAt = _timeProvider.GetUtcNow();

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var commandLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", connection, transaction))
        {
            commandLock.Parameters.AddWithValue("lock_key", NpgsqlDbType.Text, $"route-plan-command:{command.CommandId}");
            await commandLock.ExecuteNonQueryAsync(cancellationToken);
        }

        var replay = await FindCommandAsync(connection, transaction, command.CommandId, cancellationToken);
        if (replay is not null)
        {
            var replayed = replay.Value;
            var stored = await ReadVersionAsync(
                connection, transaction, replayed.RouteId, replayed.Version, cancellationToken);
            if (stored is null)
                throw new InvalidOperationException("The idempotent route plan command points to a missing snapshot.");
            await transaction.CommitAsync(cancellationToken);
            var replayOutcome = StringComparer.Ordinal.Equals(replayed.RouteId, snapshot.RouteId) &&
                                replayed.ExpectedVersion == command.ExpectedVersion &&
                                FixedHashEquals(replayed.SnapshotHash, snapshotHash)
                ? RoutePlanVersionWriteOutcome.Replayed
                : RoutePlanVersionWriteOutcome.Conflict;
            return new RoutePlanVersionWriteResult(replayOutcome, replayed.RouteId, replayed.Version);
        }

        if (command.ExpectedVersion == 0)
        {
            await using var create = new NpgsqlCommand("""
                INSERT INTO routes (route_id, current_version, created_at)
                VALUES (@route_id, 0, @created_at)
                ON CONFLICT (route_id) DO NOTHING;
                """, connection, transaction);
            create.Parameters.AddWithValue("route_id", NpgsqlDbType.Text, snapshot.RouteId);
            create.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, recordedAt);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        int? currentVersion;
        await using (var head = new NpgsqlCommand("""
            SELECT current_version FROM routes WHERE route_id = @route_id FOR UPDATE;
            """, connection, transaction))
        {
            head.Parameters.AddWithValue("route_id", NpgsqlDbType.Text, snapshot.RouteId);
            var value = await head.ExecuteScalarAsync(cancellationToken);
            currentVersion = value is null or DBNull ? null : Convert.ToInt32(value);
        }

        if (currentVersion is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new RoutePlanVersionWriteResult(
                RoutePlanVersionWriteOutcome.RouteNotFound, snapshot.RouteId, command.ExpectedVersion);
        }

        if (currentVersion.Value != command.ExpectedVersion)
        {
            await transaction.CommitAsync(cancellationToken);
            return new RoutePlanVersionWriteResult(
                RoutePlanVersionWriteOutcome.Conflict, snapshot.RouteId, currentVersion.Value);
        }

        if (command.ExpectedVersion == 0)
        {
            if (snapshot.Missions.Count != 1 || snapshot.OrderedStops.Count != 2)
                throw new ArgumentException("A route must start with one Mission and its pickup/drop-off pair.", nameof(command));
        }
        else
        {
            var previous = await ReadVersionAsync(
                connection, transaction, snapshot.RouteId, command.ExpectedVersion, cancellationToken);
            if (previous is null)
                throw new InvalidOperationException("The current route head references a missing plan version.");
            ValidateInsertionEvolution(previous.Snapshot, snapshot, command);
        }

        var nextVersion = checked(command.ExpectedVersion + 1);
        await InsertVersionAsync(
            connection, transaction, command, snapshot, snapshotHash, recordedAt, nextVersion, cancellationToken);

        await using (var advance = new NpgsqlCommand("""
            UPDATE routes SET current_version = @version WHERE route_id = @route_id;
            """, connection, transaction))
        {
            advance.Parameters.AddWithValue("route_id", NpgsqlDbType.Text, snapshot.RouteId);
            advance.Parameters.AddWithValue("version", NpgsqlDbType.Integer, nextVersion);
            if (await advance.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Route current version could not be advanced.");
        }

        await transaction.CommitAsync(cancellationToken);
        return new RoutePlanVersionWriteResult(
            RoutePlanVersionWriteOutcome.Persisted, snapshot.RouteId, nextVersion);
    }

    public async Task<RoutePlanVersion?> FindCurrentAsync(
        string routeId,
        CancellationToken cancellationToken = default)
    {
        routeId = Required(routeId, nameof(routeId));
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var head = new NpgsqlCommand(
            "SELECT current_version FROM routes WHERE route_id = @route_id;", connection);
        head.Parameters.AddWithValue("route_id", NpgsqlDbType.Text, routeId);
        var value = await head.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull || Convert.ToInt32(value) == 0)
            return null;
        var version = Convert.ToInt32(value);
        return await ReadVersionAsync(connection, null, routeId, version, cancellationToken)
            ?? throw new InvalidOperationException("Route current version points to a missing plan snapshot.");
    }

    public async Task<RoutePlanVersion?> FindVersionAsync(
        string routeId,
        int version,
        CancellationToken cancellationToken = default)
    {
        routeId = Required(routeId, nameof(routeId));
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version));
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadVersionAsync(connection, null, routeId, version, cancellationToken);
    }

    private static async Task InsertVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RoutePlanVersionWriteCommand command,
        RoutePlanSnapshot snapshot,
        string snapshotHash,
        DateTimeOffset recordedAt,
        int version,
        CancellationToken cancellationToken)
    {
        await using (var header = new NpgsqlCommand("""
            INSERT INTO route_plan_versions (
                route_id, version, command_id, expected_version, start_location_id,
                started_at, snapshot_sha256, recorded_at)
            VALUES (
                @route_id, @version, @command_id, @expected_version, @start_location_id,
                @started_at, @snapshot_sha256, @recorded_at);
            """, connection, transaction))
        {
            header.Parameters.AddWithValue("route_id", NpgsqlDbType.Text, snapshot.RouteId);
            header.Parameters.AddWithValue("version", NpgsqlDbType.Integer, version);
            header.Parameters.AddWithValue("command_id", NpgsqlDbType.Text, command.CommandId);
            header.Parameters.AddWithValue("expected_version", NpgsqlDbType.Integer, command.ExpectedVersion);
            header.Parameters.AddWithValue("start_location_id", NpgsqlDbType.Text, snapshot.StartLocationId);
            header.Parameters.AddWithValue("started_at", NpgsqlDbType.TimestampTz, snapshot.StartedAt);
            header.Parameters.AddWithValue("snapshot_sha256", NpgsqlDbType.Text, snapshotHash);
            header.Parameters.AddWithValue("recorded_at", NpgsqlDbType.TimestampTz, recordedAt);
            await header.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var mission in snapshot.Missions)
        {
            await using var insertMission = new NpgsqlCommand("""
                INSERT INTO route_plan_version_missions (
                    route_id, version, mission_id, committed_delivery_at)
                VALUES (@route_id, @version, @mission_id, @committed_delivery_at);
                """, connection, transaction);
            insertMission.Parameters.AddWithValue("route_id", NpgsqlDbType.Text, snapshot.RouteId);
            insertMission.Parameters.AddWithValue("version", NpgsqlDbType.Integer, version);
            insertMission.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, mission.MissionId);
            insertMission.Parameters.AddWithValue("committed_delivery_at", NpgsqlDbType.TimestampTz, mission.CommittedDeliveryAt);
            await insertMission.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var index = 0; index < snapshot.OrderedStops.Count; index++)
        {
            var stop = snapshot.OrderedStops[index];
            await using var insertStop = new NpgsqlCommand("""
                INSERT INTO route_plan_version_stops (
                    route_id, version, stop_order, mission_id, stop_kind, location_id,
                    service_seconds, pickup_ready_at)
                VALUES (
                    @route_id, @version, @stop_order, @mission_id, @stop_kind, @location_id,
                    @service_seconds, @pickup_ready_at);
                """, connection, transaction);
            insertStop.Parameters.AddWithValue("route_id", NpgsqlDbType.Text, snapshot.RouteId);
            insertStop.Parameters.AddWithValue("version", NpgsqlDbType.Integer, version);
            insertStop.Parameters.AddWithValue("stop_order", NpgsqlDbType.Smallint, checked((short)index));
            insertStop.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, stop.MissionId);
            insertStop.Parameters.AddWithValue("stop_kind", NpgsqlDbType.Text, stop.Kind.ToString());
            insertStop.Parameters.AddWithValue("location_id", NpgsqlDbType.Text, stop.LocationId);
            insertStop.Parameters.AddWithValue("service_seconds", NpgsqlDbType.Integer, stop.ServiceSeconds);
            insertStop.Parameters.AddWithValue("pickup_ready_at", NpgsqlDbType.TimestampTz,
                (object?)stop.PickupReadyAt ?? DBNull.Value);
            await insertStop.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<RoutePlanVersion?> ReadVersionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string routeId,
        int version,
        CancellationToken cancellationToken)
    {
        string startLocationId;
        DateTimeOffset startedAt;
        DateTimeOffset recordedAt;
        string expectedHash;
        await using (var header = new NpgsqlCommand("""
            SELECT start_location_id, started_at, snapshot_sha256, recorded_at
            FROM route_plan_versions WHERE route_id = @route_id AND version = @version;
            """, connection, transaction))
        {
            header.Parameters.AddWithValue("route_id", NpgsqlDbType.Text, routeId);
            header.Parameters.AddWithValue("version", NpgsqlDbType.Integer, version);
            await using var reader = await header.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            startLocationId = reader.GetString(0);
            startedAt = reader.GetFieldValue<DateTimeOffset>(1);
            expectedHash = reader.GetString(2);
            recordedAt = reader.GetFieldValue<DateTimeOffset>(3);
        }

        var missions = new List<RouteMissionDeadline>();
        await using (var missionCommand = new NpgsqlCommand("""
            SELECT mission_id, committed_delivery_at
            FROM route_plan_version_missions
            WHERE route_id = @route_id AND version = @version
            ORDER BY mission_id;
            """, connection, transaction))
        {
            missionCommand.Parameters.AddWithValue("route_id", NpgsqlDbType.Text, routeId);
            missionCommand.Parameters.AddWithValue("version", NpgsqlDbType.Integer, version);
            await using var reader = await missionCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                missions.Add(new RouteMissionDeadline(reader.GetString(0), reader.GetFieldValue<DateTimeOffset>(1)));
        }

        var stops = new List<RouteInsertionStop>();
        await using (var stopCommand = new NpgsqlCommand("""
            SELECT mission_id, stop_kind, location_id, service_seconds, pickup_ready_at
            FROM route_plan_version_stops
            WHERE route_id = @route_id AND version = @version
            ORDER BY stop_order;
            """, connection, transaction))
        {
            stopCommand.Parameters.AddWithValue("route_id", NpgsqlDbType.Text, routeId);
            stopCommand.Parameters.AddWithValue("version", NpgsqlDbType.Integer, version);
            await using var reader = await stopCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (!Enum.TryParse<RouteStopKind>(reader.GetString(1), ignoreCase: false, out var kind) ||
                    !Enum.IsDefined(kind))
                    throw new InvalidOperationException("Stored route stop kind is invalid.");
                stops.Add(new RouteInsertionStop(
                    reader.GetString(0), kind, reader.GetString(2), reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4)));
            }
        }

        var snapshot = new RoutePlanSnapshot(routeId, startLocationId, startedAt, missions, stops);
        try
        {
            RoutePlanSnapshotValidator.Validate(new RoutePlanVersionWriteCommand(
                "stored-snapshot-validation", Math.Max(version - 1, 0), snapshot));
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException("Stored route plan version is invalid.", exception);
        }

        if (!FixedHashEquals(expectedHash, RoutePlanSnapshotValidator.ComputeSha256(snapshot)))
            throw new InvalidOperationException("Stored route plan snapshot hash does not match.");

        return new RoutePlanVersion(version, snapshot, recordedAt);
    }

    private static async Task<(string RouteId, int Version, int ExpectedVersion, string SnapshotHash)?> FindCommandAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT route_id, version, expected_version, snapshot_sha256
            FROM route_plan_versions WHERE command_id = @command_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("command_id", NpgsqlDbType.Text, commandId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3))
            : null;
    }

    private static void ValidateInsertionEvolution(
        RoutePlanSnapshot previous,
        RoutePlanSnapshot proposed,
        RoutePlanVersionWriteCommand command)
    {
        if (!StringComparer.Ordinal.Equals(previous.RouteId, proposed.RouteId) ||
            !StringComparer.Ordinal.Equals(previous.StartLocationId, proposed.StartLocationId) ||
            previous.StartedAt != proposed.StartedAt ||
            proposed.Missions.Count != previous.Missions.Count + 1 ||
            proposed.OrderedStops.Count != previous.OrderedStops.Count + 2)
        {
            throw new ArgumentException("A route version may add exactly one Mission without changing its route origin.", nameof(command));
        }

        var proposedDeadlines = proposed.Missions.ToDictionary(item => item.MissionId, StringComparer.Ordinal);
        foreach (var mission in previous.Missions)
        {
            if (!proposedDeadlines.TryGetValue(mission.MissionId, out var next) ||
                next.CommittedDeliveryAt != mission.CommittedDeliveryAt)
                throw new ArgumentException("Existing Mission promises must remain unchanged across route versions.", nameof(command));
        }

        var previousIndex = 0;
        var insertedStops = new List<RouteInsertionStop>(2);
        foreach (var stop in proposed.OrderedStops)
        {
            if (previousIndex < previous.OrderedStops.Count && stop == previous.OrderedStops[previousIndex])
                previousIndex++;
            else
                insertedStops.Add(stop);
        }

        if (previousIndex != previous.OrderedStops.Count || insertedStops.Count != 2 ||
            insertedStops[0].MissionId != insertedStops[1].MissionId ||
            insertedStops[0].Kind != RouteStopKind.Pickup || insertedStops[1].Kind != RouteStopKind.Dropoff ||
            previous.Missions.Any(mission => mission.MissionId == insertedStops[0].MissionId))
        {
            throw new ArgumentException("A route version must preserve existing stop order and insert one new pickup/drop-off pair.", nameof(command));
        }

        var addedMission = proposed.Missions.SingleOrDefault(
            mission => previous.Missions.All(existing => existing.MissionId != mission.MissionId));
        if (addedMission is null || addedMission.MissionId != insertedStops[0].MissionId)
            throw new ArgumentException("Inserted route stops must belong to the newly added Mission.", nameof(command));
    }

    private static bool FixedHashEquals(string left, string right)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(left), Convert.FromHexString(right));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);
        return value.Trim();
    }
}
