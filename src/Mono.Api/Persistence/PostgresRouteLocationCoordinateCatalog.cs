using Mono.Api.Domain;
using Mono.Api.Services;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Append-only coordinate versions with optimistic writes and current-coordinate reads.</summary>
public sealed class PostgresRouteLocationCoordinateCatalog : IRouteLocationCoordinateCatalog
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeProvider _timeProvider;

    public PostgresRouteLocationCoordinateCatalog(NpgsqlDataSource dataSource, TimeProvider? timeProvider = null)
    {
        _dataSource = dataSource;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<RouteLocationCoordinateWriteResult> WriteAsync(
        RouteLocationCoordinateWriteCommand command,
        CancellationToken cancellationToken = default)
    {
        RouteLocationCoordinateValidator.Validate(command);
        var recordedAt = _timeProvider.GetUtcNow();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var commandLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", connection, transaction))
        {
            commandLock.Parameters.AddWithValue("lock_key", NpgsqlDbType.Text,
                $"route-location-coordinate-command:{command.CommandId}");
            await commandLock.ExecuteNonQueryAsync(cancellationToken);
        }

        var replay = await FindCommandAsync(connection, transaction, command.CommandId, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            var samePayload = replay.LocationId == command.LocationId &&
                              replay.ExpectedVersion == command.ExpectedVersion &&
                              replay.Coordinate == command.Coordinate;
            return new RouteLocationCoordinateWriteResult(
                samePayload ? RouteLocationCoordinateWriteOutcome.Replayed : RouteLocationCoordinateWriteOutcome.Conflict,
                replay.LocationId,
                replay.Version);
        }

        if (command.ExpectedVersion == 0)
        {
            await using var create = new NpgsqlCommand("""
                INSERT INTO route_location_coordinates (location_id, current_version, created_at)
                VALUES (@location_id, 0, @created_at)
                ON CONFLICT (location_id) DO NOTHING;
                """, connection, transaction);
            create.Parameters.AddWithValue("location_id", NpgsqlDbType.Text, command.LocationId);
            create.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, recordedAt);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        int? currentVersion;
        await using (var head = new NpgsqlCommand("""
            SELECT current_version FROM route_location_coordinates
            WHERE location_id = @location_id FOR UPDATE;
            """, connection, transaction))
        {
            head.Parameters.AddWithValue("location_id", NpgsqlDbType.Text, command.LocationId);
            var value = await head.ExecuteScalarAsync(cancellationToken);
            currentVersion = value is null or DBNull ? null : Convert.ToInt32(value);
        }

        if (currentVersion is null || currentVersion.Value != command.ExpectedVersion)
        {
            await transaction.CommitAsync(cancellationToken);
            return new RouteLocationCoordinateWriteResult(
                RouteLocationCoordinateWriteOutcome.Conflict,
                command.LocationId,
                currentVersion ?? 0);
        }

        var nextVersion = checked(command.ExpectedVersion + 1);
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO route_location_coordinate_versions (
                location_id, version, command_id, expected_version, latitude, longitude, recorded_at)
            VALUES (
                @location_id, @version, @command_id, @expected_version, @latitude, @longitude, @recorded_at);
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue("location_id", NpgsqlDbType.Text, command.LocationId);
            insert.Parameters.AddWithValue("version", NpgsqlDbType.Integer, nextVersion);
            insert.Parameters.AddWithValue("command_id", NpgsqlDbType.Text, command.CommandId);
            insert.Parameters.AddWithValue("expected_version", NpgsqlDbType.Integer, command.ExpectedVersion);
            insert.Parameters.AddWithValue("latitude", NpgsqlDbType.Double, command.Coordinate.Latitude);
            insert.Parameters.AddWithValue("longitude", NpgsqlDbType.Double, command.Coordinate.Longitude);
            insert.Parameters.AddWithValue("recorded_at", NpgsqlDbType.TimestampTz, recordedAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var advance = new NpgsqlCommand("""
            UPDATE route_location_coordinates SET current_version = @version
            WHERE location_id = @location_id;
            """, connection, transaction))
        {
            advance.Parameters.AddWithValue("location_id", NpgsqlDbType.Text, command.LocationId);
            advance.Parameters.AddWithValue("version", NpgsqlDbType.Integer, nextVersion);
            if (await advance.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Route location coordinate version could not be advanced.");
        }

        await transaction.CommitAsync(cancellationToken);
        return new RouteLocationCoordinateWriteResult(
            RouteLocationCoordinateWriteOutcome.Persisted, command.LocationId, nextVersion);
    }

    public async Task<IReadOnlyDictionary<string, GeoCoordinate>> ResolveAsync(
        IReadOnlyList<string> locationIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(locationIds);
        if (locationIds.Count == 0 || locationIds.Any(string.IsNullOrWhiteSpace) ||
            locationIds.Distinct(StringComparer.Ordinal).Count() != locationIds.Count)
            throw new ArgumentException("A non-empty list of unique route location IDs is required.", nameof(locationIds));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT version.location_id, version.latitude, version.longitude
            FROM route_location_coordinates AS head
            JOIN route_location_coordinate_versions AS version
              ON version.location_id = head.location_id
             AND version.version = head.current_version
            WHERE head.location_id = ANY(@location_ids)
            ORDER BY version.location_id;
            """, connection);
        query.Parameters.AddWithValue("location_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, locationIds.ToArray());

        var result = new Dictionary<string, GeoCoordinate>(StringComparer.Ordinal);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0), new GeoCoordinate(reader.GetDouble(1), reader.GetDouble(2)));
        }
        return result;
    }

    public async Task<RouteLocationCoordinateVersion?> FindVersionAsync(
        string locationId,
        int version,
        CancellationToken cancellationToken = default)
    {
        locationId = RequiredLocationId(locationId);
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var query = new NpgsqlCommand("""
            SELECT latitude, longitude, recorded_at
            FROM route_location_coordinate_versions
            WHERE location_id = @location_id AND version = @version;
            """, connection);
        query.Parameters.AddWithValue("location_id", NpgsqlDbType.Text, locationId);
        query.Parameters.AddWithValue("version", NpgsqlDbType.Integer, version);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new RouteLocationCoordinateVersion(
            locationId, version,
            new GeoCoordinate(reader.GetDouble(0), reader.GetDouble(1)),
            reader.GetFieldValue<DateTimeOffset>(2));
    }

    private static async Task<CoordinateCommandReplay?> FindCommandAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string commandId,
        CancellationToken cancellationToken)
    {
        await using var query = new NpgsqlCommand("""
            SELECT location_id, version, expected_version, latitude, longitude
            FROM route_location_coordinate_versions WHERE command_id = @command_id;
            """, connection, transaction);
        query.Parameters.AddWithValue("command_id", NpgsqlDbType.Text, commandId);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new CoordinateCommandReplay(
            reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2),
            new GeoCoordinate(reader.GetDouble(3), reader.GetDouble(4)));
    }

    private sealed record CoordinateCommandReplay(
        string LocationId, int Version, int ExpectedVersion, GeoCoordinate Coordinate);

    private static string RequiredLocationId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() ||
            value.Length > 128 || value.Any(char.IsControl))
            throw new ArgumentException("A valid route location ID is required.", nameof(value));
        return value;
    }
}
