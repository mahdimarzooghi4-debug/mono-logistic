using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public enum CourierLivePresenceOutcome
{
    Applied,
    Replayed,
    Conflict,
    CourierNotFound,
    InvalidState,
    InvalidZone
}

public sealed record CourierLivePresenceSnapshot(
    string CourierId,
    string Status,
    string? ZoneId,
    DateTimeOffset? UpdatedAt);

public sealed record CourierLivePresenceResult(
    CourierLivePresenceOutcome Outcome,
    CourierLivePresenceSnapshot? Snapshot);

/// <summary>
/// Records a courier's current self-reported operational presence. This is
/// independent of planned availability and deliberately stores no coordinates.
/// </summary>
public sealed class PostgresCourierLivePresenceService(
    NpgsqlDataSource dataSource,
    TimeProvider? timeProvider = null)
{
    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ZoneIdPattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._~-]{0,127}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex IdempotencyKeyPattern = new(
        @"\A[A-Za-z0-9._~-]{1,128}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<CourierLivePresenceSnapshot> FindAsync(
        string courierId, CancellationToken cancellationToken = default)
    {
        ValidateCourierId(courierId);
        const string sql = """
            SELECT courier.courier_id,
                CASE WHEN COALESCE(presence.is_online, FALSE) THEN 'ONLINE' ELSE 'OFFLINE' END,
                presence.zone_id, presence.changed_at
            FROM couriers courier
            LEFT JOIN courier_live_presence presence ON presence.courier_id = courier.courier_id
            WHERE courier.courier_id = @courier_id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new KeyNotFoundException("Courier was not found.");
        return ReadSnapshot(reader);
    }

    public Task<CourierLivePresenceResult> GoOnlineAsync(
        string courierId, string zoneId, string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(courierId, "ONLINE", zoneId, idempotencyKey, cancellationToken);

    public async Task<CourierLivePresenceResult> GoOfflineAsync(
        string courierId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ValidateCourierId(courierId);
        idempotencyKey = ValidateIdempotencyKey(idempotencyKey);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockIdempotencyAsync(connection, transaction, courierId, "OFFLINE", idempotencyKey, cancellationToken);
        var hash = Hash(JsonSerializer.Serialize(new { courier_id = courierId, action = "OFFLINE" }));
        var replay = await FindReplayAsync(connection, transaction, courierId, "OFFLINE", idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var state = await ReadWriteStateAsync(connection, transaction, courierId, null, cancellationToken);
        if (state is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierLivePresenceResult(CourierLivePresenceOutcome.CourierNotFound, null);
        }
        if (state.Value.Status != "ACTIVE" || state.Value.Suspended || !state.Value.IsOnline)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierLivePresenceResult(CourierLivePresenceOutcome.InvalidState, null);
        }

        var now = _timeProvider.GetUtcNow();
        var eventId = "clp_" + Guid.NewGuid().ToString("N");
        await InsertEventAsync(connection, transaction, eventId, courierId, "OFFLINE",
            state.Value.ZoneId!, now, cancellationToken);
        var snapshot = await ReadSnapshotAsync(connection, transaction, courierId, cancellationToken);
        await SaveIdempotencyAsync(connection, transaction, courierId, "OFFLINE", idempotencyKey,
            hash, snapshot, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierLivePresenceResult(CourierLivePresenceOutcome.Applied, snapshot);
    }

    private async Task<CourierLivePresenceResult> ApplyAsync(
        string courierId, string action, string zoneId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ValidateCourierId(courierId);
        zoneId = ValidateZoneId(zoneId);
        idempotencyKey = ValidateIdempotencyKey(idempotencyKey);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockIdempotencyAsync(connection, transaction, courierId, action, idempotencyKey, cancellationToken);
        var hash = Hash(JsonSerializer.Serialize(new { courier_id = courierId, action, zone_id = zoneId }));
        var replay = await FindReplayAsync(connection, transaction, courierId, action, idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var state = await ReadWriteStateAsync(connection, transaction, courierId, zoneId, cancellationToken);
        if (state is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierLivePresenceResult(CourierLivePresenceOutcome.CourierNotFound, null);
        }
        if (state.Value.Status != "ACTIVE" || state.Value.Suspended || state.Value.IsOnline)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierLivePresenceResult(CourierLivePresenceOutcome.InvalidState, null);
        }
        if (!state.Value.ZoneApproved || !state.Value.ZoneActive)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierLivePresenceResult(CourierLivePresenceOutcome.InvalidZone, null);
        }

        var now = _timeProvider.GetUtcNow();
        var eventId = "clp_" + Guid.NewGuid().ToString("N");
        await InsertEventAsync(connection, transaction, eventId, courierId, action, zoneId, now, cancellationToken);
        var snapshot = await ReadSnapshotAsync(connection, transaction, courierId, cancellationToken);
        await SaveIdempotencyAsync(connection, transaction, courierId, action, idempotencyKey,
            hash, snapshot, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierLivePresenceResult(CourierLivePresenceOutcome.Applied, snapshot);
    }

    private static async Task<(string Status, bool Suspended, bool IsOnline, string? ZoneId,
        bool ZoneApproved, bool ZoneActive)?> ReadWriteStateAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string courierId, string? zoneId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT courier.status,
                COALESCE(latest.action = 'SUSPENDED', FALSE),
                COALESCE(presence.is_online, FALSE), presence.zone_id,
                COALESCE(courier.approved_zone_ids ? @zone_id, FALSE),
                EXISTS (SELECT 1 FROM zone_geometry_active WHERE zone_id = @zone_id)
            FROM couriers courier
            LEFT JOIN courier_live_presence presence ON presence.courier_id = courier.courier_id
            LEFT JOIN LATERAL (
                SELECT action FROM courier_operational_status_events
                WHERE courier_id = courier.courier_id ORDER BY event_sequence DESC LIMIT 1
            ) latest ON TRUE
            WHERE courier.courier_id = @courier_id
            FOR UPDATE OF courier;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, (object?)zoneId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return (reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.GetBoolean(4), reader.GetBoolean(5));
    }

    private static async Task<CourierLivePresenceSnapshot> ReadSnapshotAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string courierId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT courier.courier_id,
                CASE WHEN COALESCE(presence.is_online, FALSE) THEN 'ONLINE' ELSE 'OFFLINE' END,
                presence.zone_id, presence.changed_at
            FROM couriers courier
            LEFT JOIN courier_live_presence presence ON presence.courier_id = courier.courier_id
            WHERE courier.courier_id = @courier_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Courier disappeared during presence update.");
        return ReadSnapshot(reader);
    }

    private static CourierLivePresenceSnapshot ReadSnapshot(NpgsqlDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));

    private static async Task InsertEventAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string eventId,
        string courierId, string action, string zoneId, DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO courier_live_presence_events
                (event_id, courier_id, action, zone_id, occurred_at)
            VALUES (@event_id, @courier_id, @action, @zone_id, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockIdempotencyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string courierId, string operation, string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", connection, transaction);
        command.Parameters.AddWithValue("lock_key", NpgsqlDbType.Text, $"{courierId}:{operation}:{key}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CourierLivePresenceResult?> FindReplayAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string courierId, string operation, string key, string hash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT request_hash, response FROM courier_live_presence_idempotency
            WHERE courier_id = @courier_id AND operation = @operation AND idempotency_key = @key;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var storedHash = reader.GetString(0);
        var json = reader.GetString(1);
        await reader.CloseAsync();
        return string.Equals(storedHash, hash, StringComparison.Ordinal)
            ? new CourierLivePresenceResult(CourierLivePresenceOutcome.Replayed,
                JsonSerializer.Deserialize<CourierLivePresenceSnapshot>(json))
            : new CourierLivePresenceResult(CourierLivePresenceOutcome.Conflict, null);
    }

    private static async Task SaveIdempotencyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string courierId, string operation, string key, string hash,
        CourierLivePresenceSnapshot snapshot, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO courier_live_presence_idempotency
                (courier_id, operation, idempotency_key, request_hash, response, created_at)
            VALUES (@courier_id, @operation, @key, @hash, @response::jsonb, @created_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, key);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Text, hash);
        command.Parameters.AddWithValue("response", NpgsqlDbType.Text, JsonSerializer.Serialize(snapshot));
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ValidateCourierId(string value)
    {
        if (!CourierIdPattern.IsMatch(value))
            throw new ArgumentException("Courier ID is invalid.", nameof(value));
        return value;
    }

    private static string ValidateZoneId(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (!ZoneIdPattern.IsMatch(value))
            throw new ArgumentException("Zone ID is invalid.", nameof(value));
        return value;
    }

    private static string ValidateIdempotencyKey(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (!IdempotencyKeyPattern.IsMatch(value))
            throw new ArgumentException("Idempotency key is invalid.", nameof(value));
        return value;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
