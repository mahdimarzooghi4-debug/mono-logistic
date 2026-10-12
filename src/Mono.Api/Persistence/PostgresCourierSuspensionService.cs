using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Audited, idempotent courier suspension and reinstatement operations.</summary>
public sealed class PostgresCourierSuspensionService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    TimeProvider? timeProvider = null)
{
    private const string SuspendOperation = "SUSPEND";
    private const string ReinstateOperation = "REINSTATE";
    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReasonCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<CourierOperationalStatusResult> SuspendAsync(
        string courierId, string reasonCode, string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(courierId, SuspendOperation, reasonCode, idempotencyKey, cancellationToken);

    public Task<CourierOperationalStatusResult> ReinstateAsync(
        string courierId, string reasonCode, string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ApplyAsync(courierId, ReinstateOperation, reasonCode, idempotencyKey, cancellationToken);

    public async Task<CourierOperationalStatusSnapshot?> FindAsync(
        string courierId, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        ValidateCourierId(courierId);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadSnapshotAsync(connection, null, courierId, cancellationToken);
    }

    private async Task<CourierOperationalStatusResult> ApplyAsync(
        string courierId, string operation, string reasonCode, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var actorId = RequireAdministrator();
        ValidateCourierId(courierId);
        reasonCode = reasonCode?.Trim() ?? string.Empty;
        if (!ReasonCodePattern.IsMatch(reasonCode))
            throw new ArgumentException("A canonical reason code is required.", nameof(reasonCode));
        idempotencyKey = ValidateIdempotencyKey(idempotencyKey);
        var hash = Hash(JsonSerializer.Serialize(new { courier_id = courierId, operation, reason_code = reasonCode }));
        var now = _timeProvider.GetUtcNow();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockIdempotencyAsync(connection, transaction, actorId, operation, idempotencyKey, cancellationToken);

        const string replaySql = """
            SELECT request_hash FROM courier_operational_status_idempotency
            WHERE actor_id = @actor_id AND operation = @operation AND idempotency_key = @key;
            """;
        await using (var command = new NpgsqlCommand(replaySql, connection, transaction))
        {
            command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
            command.Parameters.AddWithValue("key", NpgsqlDbType.Text, idempotencyKey);
            var storedHash = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (storedHash is not null)
            {
                if (!string.Equals(storedHash, hash, StringComparison.Ordinal))
                {
                    await transaction.CommitAsync(cancellationToken);
                    return new CourierOperationalStatusResult(CourierOperationalStatusOutcome.Conflict, null);
                }
                var replaySnapshot = await ReadSnapshotAsync(connection, transaction, courierId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new CourierOperationalStatusResult(
                    CourierOperationalStatusOutcome.Replayed, replaySnapshot);
            }
        }

        const string courierSql = "SELECT status FROM couriers WHERE courier_id = @courier_id FOR UPDATE;";
        string? trustStatus;
        await using (var command = new NpgsqlCommand(courierSql, connection, transaction))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            trustStatus = await command.ExecuteScalarAsync(cancellationToken) as string;
        }
        if (trustStatus is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierOperationalStatusResult(CourierOperationalStatusOutcome.CourierNotFound, null);
        }
        if (trustStatus != "ACTIVE")
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierOperationalStatusResult(CourierOperationalStatusOutcome.InvalidState, null);
        }

        const string latestSql = """
            SELECT action FROM courier_operational_status_events
            WHERE courier_id = @courier_id ORDER BY event_sequence DESC LIMIT 1;
            """;
        string? latestAction;
        await using (var command = new NpgsqlCommand(latestSql, connection, transaction))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            latestAction = await command.ExecuteScalarAsync(cancellationToken) as string;
        }
        var suspended = latestAction == "SUSPENDED";
        if ((operation == SuspendOperation && suspended) ||
            (operation == ReinstateOperation && !suspended))
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierOperationalStatusResult(CourierOperationalStatusOutcome.InvalidState, null);
        }

        var eventId = "cse_" + Guid.NewGuid().ToString("N");
        const string insertEventSql = """
            INSERT INTO courier_operational_status_events
                (event_id, courier_id, action, actor_id, reason_code, occurred_at)
            VALUES (@event_id, @courier_id, @action, @actor_id, @reason_code, @occurred_at);
            """;
        await using (var command = new NpgsqlCommand(insertEventSql, connection, transaction))
        {
            command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("action", NpgsqlDbType.Text,
                operation == SuspendOperation ? "SUSPENDED" : "REINSTATED");
            command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            command.Parameters.AddWithValue("reason_code", NpgsqlDbType.Text, reasonCode);
            command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if (operation == SuspendOperation)
        {
            const string cancelWindowsSql = """
                UPDATE courier_availability_windows
                SET status = 'CANCELLED', cancelled_by = @actor_id,
                    cancelled_at = @occurred_at, cancellation_reason = 'COURIER_SUSPENDED'
                WHERE courier_id = @courier_id AND status = 'SCHEDULED'
                  AND ends_at > @occurred_at;
                """;
            await using var cancelWindows = new NpgsqlCommand(cancelWindowsSql, connection, transaction);
            cancelWindows.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            cancelWindows.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
            cancelWindows.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            await cancelWindows.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertIdempotencySql = """
            INSERT INTO courier_operational_status_idempotency
                (actor_id, operation, idempotency_key, request_hash, event_id, created_at)
            VALUES (@actor_id, @operation, @key, @request_hash, @event_id, @created_at);
            """;
        await using (var command = new NpgsqlCommand(insertIdempotencySql, connection, transaction))
        {
            command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
            command.Parameters.AddWithValue("key", NpgsqlDbType.Text, idempotencyKey);
            command.Parameters.AddWithValue("request_hash", NpgsqlDbType.Text, hash);
            command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var snapshot = await ReadSnapshotAsync(connection, transaction, courierId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierOperationalStatusResult(CourierOperationalStatusOutcome.Applied, snapshot);
    }

    private static async Task<CourierOperationalStatusSnapshot?> ReadSnapshotAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, string courierId,
        CancellationToken cancellationToken)
    {
        const string courierSql = "SELECT status FROM couriers WHERE courier_id = @courier_id;";
        string? trustStatus;
        await using (var command = new NpgsqlCommand(courierSql, connection, transaction))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            trustStatus = await command.ExecuteScalarAsync(cancellationToken) as string;
        }
        if (trustStatus is null)
            return null;

        const string historySql = """
            SELECT event_id, action, actor_id, reason_code, occurred_at
            FROM courier_operational_status_events
            WHERE courier_id = @courier_id
            ORDER BY event_sequence DESC LIMIT 50;
            """;
        var history = new List<CourierOperationalStatusEvent>();
        await using (var command = new NpgsqlCommand(historySql, connection, transaction))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                history.Add(new CourierOperationalStatusEvent(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetString(3), reader.GetFieldValue<DateTimeOffset>(4)));
        }
        var latest = history.FirstOrDefault();
        var operationalStatus = trustStatus != "ACTIVE" ? "NOT_ACTIVE" :
            latest?.Action == "SUSPENDED" ? "SUSPENDED" : "ACTIVE";
        return new CourierOperationalStatusSnapshot(
            courierId, trustStatus, operationalStatus, latest?.OccurredAt, history);
    }

    private static async Task LockIdempotencyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string actorId,
        string operation, string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", connection, transaction);
        command.Parameters.AddWithValue("lock_key", NpgsqlDbType.Text, $"{actorId}:{operation}:{key}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ValidateIdempotencyKey(string? value)
    {
        var key = value?.Trim() ?? string.Empty;
        if (key.Length is < 1 or > 128 || key.Any(char.IsControl))
            throw new ArgumentException("A valid Idempotency-Key is required.", nameof(value));
        return key;
    }

    private static void ValidateCourierId(string value)
    {
        if (!CourierIdPattern.IsMatch(value))
            throw new ArgumentException("Courier ID is invalid.", nameof(value));
    }

    private string RequireAdministrator()
    {
        var actorId = operationsContext.ActorId;
        if (string.IsNullOrWhiteSpace(actorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return actorId;
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
