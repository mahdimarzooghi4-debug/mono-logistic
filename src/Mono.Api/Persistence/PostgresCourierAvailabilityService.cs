using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>
/// Stores planned, staff-entered courier duty windows. A planned window is not
/// real-time availability and does not make a courier dispatch eligible.
/// </summary>
public sealed class PostgresCourierAvailabilityService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    PostgresCourierOperationsReadAuditService readAudit,
    TimeProvider? timeProvider = null)
{
    private const string CreateOperation = "CREATE_WINDOW";
    private const string CancelOperation = "CANCEL_WINDOW";
    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex WindowIdPattern = new(
        @"\Acaw_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<CourierAvailabilityWriteResult> CreateAsync(
        string courierId,
        string zoneId,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        ValidateCourierId(courierId);
        zoneId = ValidateZoneId(zoneId);
        idempotencyKey = ValidateIdempotencyKey(idempotencyKey);
        var now = _timeProvider.GetUtcNow();
        if (startsAt.Offset != TimeSpan.Zero || endsAt.Offset != TimeSpan.Zero ||
            startsAt < now || startsAt > now.AddDays(30) ||
            endsAt <= startsAt || endsAt - startsAt > TimeSpan.FromHours(16))
            throw new ArgumentException("Availability window must be in the next 30 days and no longer than 16 hours.");

        var requestHash = Hash(JsonSerializer.Serialize(new
        {
            courier_id = courierId, zone_id = zoneId, starts_at = startsAt, ends_at = endsAt
        }));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockIdempotencyAsync(connection, transaction, actorId, CreateOperation, idempotencyKey, cancellationToken);
        var replay = await ReadReplayAsync(
            connection, transaction, actorId, CreateOperation, idempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        const string courierSql = """
            SELECT status, approved_zone_ids ? @zone_id
            FROM couriers WHERE courier_id = @courier_id FOR UPDATE;
            """;
        string? courierStatus = null;
        var zoneApproved = false;
        await using (var command = new NpgsqlCommand(courierSql, connection, transaction))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                courierStatus = reader.GetString(0);
                zoneApproved = reader.GetBoolean(1);
            }
        }
        if (courierStatus is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierAvailabilityWriteResult(CourierAvailabilityWriteOutcome.CourierNotFound, null);
        }
        if (courierStatus != "ACTIVE")
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierAvailabilityWriteResult(CourierAvailabilityWriteOutcome.InvalidCourierState, null);
        }
        if (!zoneApproved || !await IsActiveZoneAsync(connection, transaction, zoneId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierAvailabilityWriteResult(CourierAvailabilityWriteOutcome.InvalidZone, null);
        }

        const string overlapSql = """
            SELECT EXISTS (
                SELECT 1 FROM courier_availability_windows
                WHERE courier_id = @courier_id AND status = 'SCHEDULED'
                  AND starts_at < @ends_at AND ends_at > @starts_at);
            """;
        await using (var command = new NpgsqlCommand(overlapSql, connection, transaction))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("starts_at", NpgsqlDbType.TimestampTz, startsAt);
            command.Parameters.AddWithValue("ends_at", NpgsqlDbType.TimestampTz, endsAt);
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false))
            {
                await transaction.CommitAsync(cancellationToken);
                return new CourierAvailabilityWriteResult(CourierAvailabilityWriteOutcome.Overlap, null);
            }
        }

        var window = new CourierAvailabilityWindow(
            "caw_" + Guid.NewGuid().ToString("N"), courierId, zoneId, startsAt, endsAt,
            "SCHEDULED", actorId, now, null, null, null);
        const string insertSql = """
            INSERT INTO courier_availability_windows
                (window_id, courier_id, zone_id, starts_at, ends_at, status, created_by, created_at)
            VALUES (@window_id, @courier_id, @zone_id, @starts_at, @ends_at, 'SCHEDULED', @actor_id, @now);
            """;
        await using (var command = new NpgsqlCommand(insertSql, connection, transaction))
        {
            AddWindowParameters(command, window);
            command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await SaveIdempotencyAsync(
            connection, transaction, actorId, CreateOperation, idempotencyKey, requestHash, window, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierAvailabilityWriteResult(CourierAvailabilityWriteOutcome.Applied, window);
    }

    public async Task<CourierAvailabilityWriteResult> CancelAsync(
        string windowId,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        if (!WindowIdPattern.IsMatch(windowId))
            throw new ArgumentException("Availability window ID is invalid.", nameof(windowId));
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length is < 2 or > 128 || reason.Any(char.IsControl))
            throw new ArgumentException("Cancellation reason must be between 2 and 128 characters.", nameof(reason));
        idempotencyKey = ValidateIdempotencyKey(idempotencyKey);
        var requestHash = Hash(JsonSerializer.Serialize(new { window_id = windowId, reason }));
        var now = _timeProvider.GetUtcNow();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockIdempotencyAsync(connection, transaction, actorId, CancelOperation, idempotencyKey, cancellationToken);
        var replay = await ReadReplayAsync(
            connection, transaction, actorId, CancelOperation, idempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        const string sql = """
            UPDATE courier_availability_windows
            SET status = 'CANCELLED', cancelled_by = @actor_id,
                cancelled_at = @now, cancellation_reason = @reason
            WHERE window_id = @window_id AND status = 'SCHEDULED'
            RETURNING window_id, courier_id, zone_id, starts_at, ends_at, status,
                created_by, created_at, cancelled_by, cancelled_at, cancellation_reason;
            """;
        CourierAvailabilityWindow? window = null;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("window_id", NpgsqlDbType.Text, windowId);
            command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            command.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                window = ReadWindow(reader);
        }
        if (window is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierAvailabilityWriteResult(CourierAvailabilityWriteOutcome.WindowNotFound, null);
        }
        await SaveIdempotencyAsync(
            connection, transaction, actorId, CancelOperation, idempotencyKey, requestHash, window, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierAvailabilityWriteResult(CourierAvailabilityWriteOutcome.Applied, window);
    }

    public async Task<CourierAvailabilityWindowPage?> ListAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        int pageSize,
        string? zoneId,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        if (from.Offset != TimeSpan.Zero || to.Offset != TimeSpan.Zero ||
            to <= from || to - from > TimeSpan.FromDays(31) ||
            pageSize is < 1 or > 100 || !IsValidZoneFilter(zoneId) ||
            (cursor is not null && !WindowIdPattern.IsMatch(cursor)))
            throw new ArgumentException("Courier availability search filters are invalid.");
        zoneId = string.IsNullOrWhiteSpace(zoneId) ? null : zoneId.Trim();

        DateTimeOffset? cursorStartsAt = null;
        if (cursor is not null)
        {
            await using var cursorConnection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var cursorCommand = new NpgsqlCommand("""
                SELECT starts_at, ends_at, zone_id
                FROM courier_availability_windows WHERE window_id = @window_id;
                """, cursorConnection);
            cursorCommand.Parameters.AddWithValue("window_id", NpgsqlDbType.Text, cursor);
            await using var reader = await cursorCommand.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            cursorStartsAt = reader.GetFieldValue<DateTimeOffset>(0);
            var cursorEndsAt = reader.GetFieldValue<DateTimeOffset>(1);
            var cursorZone = reader.GetString(2);
            if (cursorStartsAt >= to || cursorEndsAt <= from ||
                (zoneId is not null && !string.Equals(zoneId, cursorZone, StringComparison.Ordinal)))
                return null;
        }

        const string firstPageSql = """
            SELECT window_id, courier_id, zone_id, starts_at, ends_at, status,
                created_by, created_at, cancelled_by, cancelled_at, cancellation_reason
            FROM courier_availability_windows
            WHERE starts_at < @to AND ends_at > @from
              AND (@zone_id IS NULL OR zone_id = @zone_id)
            ORDER BY starts_at, window_id LIMIT @take;
            """;
        const string nextPageSql = """
            SELECT window_id, courier_id, zone_id, starts_at, ends_at, status,
                created_by, created_at, cancelled_by, cancelled_at, cancellation_reason
            FROM courier_availability_windows
            WHERE starts_at < @to AND ends_at > @from
              AND (@zone_id IS NULL OR zone_id = @zone_id)
              AND (starts_at > @cursor_starts_at OR
                   (starts_at = @cursor_starts_at AND window_id > @cursor_id))
            ORDER BY starts_at, window_id LIMIT @take;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(cursor is null ? firstPageSql : nextPageSql, connection);
        command.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, from);
        command.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, to);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, (object?)zoneId ?? DBNull.Value);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
        if (cursor is not null)
        {
            command.Parameters.AddWithValue("cursor_starts_at", NpgsqlDbType.TimestampTz, cursorStartsAt!.Value);
            command.Parameters.AddWithValue("cursor_id", NpgsqlDbType.Text, cursor);
        }

        var items = new List<CourierAvailabilityWindow>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                items.Add(ReadWindow(reader));

        string? nextCursor = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            nextCursor = items[^1].WindowId;
        }
        await readAudit.RecordAsync(
            PostgresCourierOperationsReadAuditService.CourierAvailabilitySearch,
            null, items.Count, cancellationToken);
        return new CourierAvailabilityWindowPage(items, nextCursor);
    }

    private async Task<bool> IsActiveZoneAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string zoneId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM zone_geometry_active WHERE zone_id = @zone_id);", connection, transaction);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private async Task LockIdempotencyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string actorId, string operation, string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", connection, transaction);
        command.Parameters.AddWithValue("lock_key", NpgsqlDbType.Text, $"{actorId}:{operation}:{key}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CourierAvailabilityWriteResult?> ReadReplayAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string actorId,
        string operation, string key, string requestHash, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT request_hash, response FROM courier_availability_idempotency
            WHERE actor_id = @actor_id AND operation = @operation AND idempotency_key = @key;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        if (!string.Equals(reader.GetString(0), requestHash, StringComparison.Ordinal))
            return new CourierAvailabilityWriteResult(CourierAvailabilityWriteOutcome.Conflict, null);
        var window = JsonSerializer.Deserialize<CourierAvailabilityWindow>(reader.GetString(1))
            ?? throw new InvalidOperationException("Courier availability idempotency response is invalid.");
        return new CourierAvailabilityWriteResult(CourierAvailabilityWriteOutcome.Replayed, window);
    }

    private static async Task SaveIdempotencyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string actorId, string operation,
        string key, string requestHash, CourierAvailabilityWindow window, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO courier_availability_idempotency
                (actor_id, operation, idempotency_key, request_hash, response, created_at)
            VALUES (@actor_id, @operation, @key, @request_hash, @response, @now);
            """, connection, transaction);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, key);
        command.Parameters.AddWithValue("request_hash", NpgsqlDbType.Text, requestHash);
        command.Parameters.AddWithValue("response", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(window));
        command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddWindowParameters(NpgsqlCommand command, CourierAvailabilityWindow window)
    {
        command.Parameters.AddWithValue("window_id", NpgsqlDbType.Text, window.WindowId);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, window.CourierId);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, window.ZoneId);
        command.Parameters.AddWithValue("starts_at", NpgsqlDbType.TimestampTz, window.StartsAt);
        command.Parameters.AddWithValue("ends_at", NpgsqlDbType.TimestampTz, window.EndsAt);
    }

    private static CourierAvailabilityWindow ReadWindow(NpgsqlDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2),
        reader.GetFieldValue<DateTimeOffset>(3), reader.GetFieldValue<DateTimeOffset>(4),
        reader.GetString(5), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7),
        reader.IsDBNull(8) ? null : reader.GetString(8),
        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
        reader.IsDBNull(10) ? null : reader.GetString(10));

    private string RequireAdministrator()
    {
        var actorId = operationsContext.ActorId;
        if (string.IsNullOrWhiteSpace(actorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return actorId;
    }

    private static void ValidateCourierId(string value)
    {
        if (!CourierIdPattern.IsMatch(value))
            throw new ArgumentException("Courier ID is invalid.", nameof(value));
    }

    private static string ValidateZoneId(string value)
    {
        var zone = value?.Trim() ?? string.Empty;
        if (zone.Length is < 1 or > 128 || zone.Any(char.IsControl))
            throw new ArgumentException("Zone ID is invalid.", nameof(value));
        return zone;
    }

    private static bool IsValidZoneFilter(string? value) =>
        value is null || value.Length is >= 1 and <= 128 && !value.Any(char.IsControl);

    private static string ValidateIdempotencyKey(string? value)
    {
        var key = value?.Trim() ?? string.Empty;
        if (key.Length is < 1 or > 128 || key.Any(char.IsControl))
            throw new ArgumentException("A valid Idempotency-Key is required.", nameof(value));
        return key;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
