using Mono.Api.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierCurrentLocationWriteResult(CourierCurrentPosition Position, bool Applied);

/// <summary>Maintains only the latest GPS point per courier; stale updates cannot move it backwards.</summary>
public sealed class PostgresCourierCurrentLocationService(
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider)
{
    public async Task ClearAsync(string courierId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(courierId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "DELETE FROM courier_current_locations WHERE courier_id = @courier_id;", connection);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CourierCurrentLocationWriteResult> UpdateAsync(
        string courierId,
        CourierLocationUpdate update,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(courierId);
        ArgumentNullException.ThrowIfNull(update);
        var normalized = (update with { CourierId = courierId }).Normalize();
        var errors = CourierLocationUpdateValidator.Validate(normalized);
        if (errors.Count > 0)
            throw new ArgumentException("Courier location fields are invalid.", nameof(update));

        const string upsert = """
            INSERT INTO courier_current_locations (
                courier_id, latitude, longitude, accuracy_meters, heading_degrees,
                speed_meters_per_second, observed_at, active_route_id, updated_at)
            VALUES (@courier_id, @latitude, @longitude, @accuracy, @heading,
                @speed, @observed_at, @active_route_id, @updated_at)
            ON CONFLICT (courier_id) DO UPDATE SET
                latitude = EXCLUDED.latitude,
                longitude = EXCLUDED.longitude,
                accuracy_meters = EXCLUDED.accuracy_meters,
                heading_degrees = EXCLUDED.heading_degrees,
                speed_meters_per_second = EXCLUDED.speed_meters_per_second,
                observed_at = EXCLUDED.observed_at,
                active_route_id = EXCLUDED.active_route_id,
                updated_at = EXCLUDED.updated_at
            WHERE courier_current_locations.observed_at < EXCLUDED.observed_at
            RETURNING courier_id, latitude, longitude, accuracy_meters, heading_degrees,
                speed_meters_per_second, observed_at, active_route_id;
            """;
        const string readCurrent = """
            SELECT courier_id, latitude, longitude, accuracy_meters, heading_degrees,
                speed_meters_per_second, observed_at, active_route_id
            FROM courier_current_locations WHERE courier_id = @courier_id;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        CourierCurrentPosition? position;
        var applied = false;
        await using (var command = new NpgsqlCommand(upsert, connection))
        {
            AddParameters(command, courierId, normalized, timeProvider.GetUtcNow());
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                position = ReadPosition(reader);
                applied = true;
            }
            else position = null;
        }

        if (position is null)
        {
            await using var command = new NpgsqlCommand(readCurrent, connection);
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new KeyNotFoundException("Courier was not found.");
            position = ReadPosition(reader);
        }

        return new CourierCurrentLocationWriteResult(position, applied);
    }

    private static void AddParameters(NpgsqlCommand command, string courierId,
        CourierLocationUpdate update, DateTimeOffset now)
    {
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        command.Parameters.AddWithValue("latitude", NpgsqlDbType.Double, update.Latitude);
        command.Parameters.AddWithValue("longitude", NpgsqlDbType.Double, update.Longitude);
        AddNullable(command, "accuracy", NpgsqlDbType.Double, update.AccuracyMeters);
        AddNullable(command, "heading", NpgsqlDbType.Double, update.HeadingDegrees);
        AddNullable(command, "speed", NpgsqlDbType.Double, update.SpeedMetersPerSecond);
        command.Parameters.AddWithValue("observed_at", NpgsqlDbType.TimestampTz, update.ObservedAt!.Value);
        AddNullable(command, "active_route_id", NpgsqlDbType.Text, update.ActiveRouteId);
        command.Parameters.AddWithValue("updated_at", NpgsqlDbType.TimestampTz, now);
    }

    private static void AddNullable(NpgsqlCommand command, string name, NpgsqlDbType type, object? value) =>
        command.Parameters.AddWithValue(name, type, value ?? DBNull.Value);

    private static CourierCurrentPosition ReadPosition(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        new GeoCoordinate(reader.GetDouble(1), reader.GetDouble(2)),
        reader.IsDBNull(3) ? null : reader.GetDouble(3),
        reader.IsDBNull(4) ? null : reader.GetDouble(4),
        reader.IsDBNull(5) ? null : reader.GetDouble(5),
        reader.GetFieldValue<DateTimeOffset>(6).ToUniversalTime(),
        reader.IsDBNull(7) ? null : reader.GetString(7));
}
