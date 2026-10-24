using System.Security.Cryptography;
using System.Text;
using Mono.Api.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record WebhookDeliveryClaim(
    string DeliveryId,
    string EventId,
    string EventType,
    string EndpointId,
    string CustomerId,
    string Url,
    string SecretCiphertext,
    string Payload,
    int AttemptNumber);

public sealed record WebhookDeliveryResult(int? HttpStatus, string? ErrorCode)
{
    public bool IsSuccess => HttpStatus is >= 200 and < 300;
    public bool IsRetryable => ErrorCode is "NETWORK_ERROR" or "TIMEOUT" || HttpStatus is 408 or 429 or >= 500;
}

public sealed class PostgresWebhookDeliveryRepository(NpgsqlDataSource dataSource)
{
    public async Task<int> EnqueueAsync(
        IReadOnlyCollection<string> supportedEventTypes,
        int batchSize,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string selectSql = """
            SELECT e.event_id, e.event_type, e.event_version, e.occurred_at, e.correlation_id,
                   e.customer_id, e.aggregate_type, e.aggregate_id, e.sequence, e.data::text, e.created_at,
                   endpoint.endpoint_id
            FROM outbox_events e
            JOIN webhook_endpoints endpoint
              ON endpoint.customer_id = e.customer_id
             AND endpoint.status = 'ACTIVE'
             AND e.event_type = ANY(endpoint.events)
             AND e.created_at >= endpoint.created_at
            WHERE e.event_type = ANY(@supported_event_types)
              AND NOT EXISTS (
                  SELECT 1 FROM webhook_deliveries delivery
                  WHERE delivery.endpoint_id = endpoint.endpoint_id AND delivery.event_id = e.event_id)
            ORDER BY e.created_at, e.event_id, endpoint.endpoint_id
            LIMIT @batch_size;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var candidates = new List<(WebhookOutboxEvent Event, string EndpointId)>();
        await using (var command = new NpgsqlCommand(selectSql, connection, transaction))
        {
            command.Parameters.AddWithValue("supported_event_types", NpgsqlDbType.Array | NpgsqlDbType.Text,
                supportedEventTypes.ToArray());
            command.Parameters.AddWithValue("batch_size", NpgsqlDbType.Integer, batchSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                candidates.Add((new WebhookOutboxEvent(
                    reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                    reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4), reader.GetString(5),
                    reader.GetString(6), reader.GetString(7), reader.GetInt64(8), reader.GetString(9),
                    reader.GetFieldValue<DateTimeOffset>(10)), reader.GetString(11)));
            }
        }

        const string insertSql = """
            INSERT INTO webhook_deliveries (
                delivery_id, endpoint_id, customer_id, event_id, event_type, payload,
                status, attempt_count, next_attempt_at, created_at)
            VALUES (
                @delivery_id, @endpoint_id, @customer_id, @event_id, @event_type, @payload,
                'PENDING', 0, @next_attempt_at, @created_at)
            ON CONFLICT DO NOTHING;
            """;
        var queued = 0;
        foreach (var candidate in candidates)
        {
            await using var command = new NpgsqlCommand(insertSql, connection, transaction);
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, CreateDeliveryId(candidate.Event.EventId, candidate.EndpointId));
            command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, candidate.EndpointId);
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, candidate.Event.CustomerId);
            command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, candidate.Event.EventId);
            command.Parameters.AddWithValue("event_type", NpgsqlDbType.Text, candidate.Event.EventType);
            command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, WebhookEventPayload.Create(candidate.Event));
            command.Parameters.AddWithValue("next_attempt_at", NpgsqlDbType.TimestampTz, now);
            command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            queued += await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return queued;
    }

    public async Task<IReadOnlyList<WebhookDeliveryClaim>> ClaimDueAsync(
        int batchSize,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await RescheduleExpiredLeasesAsync(connection, transaction, now, cancellationToken);

        const string claimSql = """
            WITH due AS (
                SELECT delivery.delivery_id, endpoint.url, endpoint.secret_ciphertext
                FROM webhook_deliveries delivery
                JOIN webhook_endpoints endpoint
                  ON endpoint.customer_id = delivery.customer_id
                 AND endpoint.endpoint_id = delivery.endpoint_id
                 AND endpoint.status = 'ACTIVE'
                WHERE delivery.status IN ('PENDING', 'RETRYING')
                  AND delivery.next_attempt_at <= @now
                  AND delivery.attempt_count < 5
                ORDER BY delivery.next_attempt_at, delivery.created_at, delivery.delivery_id
                LIMIT @batch_size
                FOR UPDATE OF delivery SKIP LOCKED
            )
            UPDATE webhook_deliveries delivery
            SET status = 'IN_PROGRESS',
                attempt_count = delivery.attempt_count + 1,
                lease_expires_at = @lease_expires_at,
                last_attempt_at = @now
            FROM due
            WHERE delivery.delivery_id = due.delivery_id
            RETURNING delivery.delivery_id, delivery.event_id, delivery.event_type,
                      delivery.endpoint_id, delivery.customer_id, due.url, due.secret_ciphertext,
                      delivery.payload::text, delivery.attempt_count;
            """;
        var claims = new List<WebhookDeliveryClaim>();
        await using (var command = new NpgsqlCommand(claimSql, connection, transaction))
        {
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            command.Parameters.AddWithValue("lease_expires_at", NpgsqlDbType.TimestampTz, now + leaseDuration);
            command.Parameters.AddWithValue("batch_size", NpgsqlDbType.Integer, batchSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                claims.Add(new WebhookDeliveryClaim(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7),
                    reader.GetInt32(8)));
            }
        }

        foreach (var claim in claims)
            await AppendAttemptEventAsync(connection, transaction, claim.DeliveryId, claim.AttemptNumber,
                "STARTED", null, null, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claims;
    }

    public async Task CompleteAttemptAsync(
        WebhookDeliveryClaim claim,
        WebhookDeliveryResult result,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var success = result.IsSuccess;
        var retry = !success && result.IsRetryable && claim.AttemptNumber < 5;
        var status = success ? "SUCCEEDED" : retry ? "RETRYING" : "DEAD_LETTERED";
        var action = success ? "SUCCEEDED" : retry ? "RETRY_SCHEDULED" : "DEAD_LETTERED";
        var nextAttemptAt = retry ? now + RetryDelay(claim.AttemptNumber) : now;
        var errorCode = success ? null : result.ErrorCode ?? (result.HttpStatus is int code ? $"HTTP_{code}" : "DELIVERY_FAILED");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string sql = """
            UPDATE webhook_deliveries
            SET status = @status, lease_expires_at = NULL, next_attempt_at = @next_attempt_at,
                last_http_status = @http_status, last_error_code = @error_code,
                completed_at = CASE WHEN @terminal THEN @now ELSE NULL END
            WHERE delivery_id = @delivery_id AND status = 'IN_PROGRESS' AND attempt_count = @attempt_number;
            """;
        int changed;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("status", NpgsqlDbType.Text, status);
            command.Parameters.AddWithValue("next_attempt_at", NpgsqlDbType.TimestampTz, nextAttemptAt);
            command.Parameters.AddWithValue("http_status", NpgsqlDbType.Integer, (object?)result.HttpStatus ?? DBNull.Value);
            command.Parameters.AddWithValue("error_code", NpgsqlDbType.Text, (object?)errorCode ?? DBNull.Value);
            command.Parameters.AddWithValue("terminal", NpgsqlDbType.Boolean, !retry);
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, claim.DeliveryId);
            command.Parameters.AddWithValue("attempt_number", NpgsqlDbType.Integer, claim.AttemptNumber);
            changed = await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (changed == 1)
            await AppendAttemptEventAsync(connection, transaction, claim.DeliveryId, claim.AttemptNumber,
                action, result.HttpStatus, errorCode, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task RescheduleExpiredLeasesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string selectSql = """
            SELECT delivery_id, attempt_count
            FROM webhook_deliveries
            WHERE status = 'IN_PROGRESS' AND lease_expires_at <= @now
            ORDER BY lease_expires_at, delivery_id
            LIMIT 100
            FOR UPDATE SKIP LOCKED
            """;
        var expired = new List<(string DeliveryId, int AttemptNumber)>();
        await using (var command = new NpgsqlCommand(selectSql, connection, transaction))
        {
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                expired.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        foreach (var item in expired)
        {
            var exhausted = item.AttemptNumber >= 5;
            const string updateSql = """
                UPDATE webhook_deliveries
                SET status = @status, lease_expires_at = NULL, next_attempt_at = @next_attempt_at,
                    last_error_code = 'LEASE_EXPIRED', completed_at = @completed_at
                WHERE delivery_id = @delivery_id AND status = 'IN_PROGRESS';
                """;
            await using (var update = new NpgsqlCommand(updateSql, connection, transaction))
            {
                update.Parameters.AddWithValue("status", NpgsqlDbType.Text, exhausted ? "DEAD_LETTERED" : "RETRYING");
                update.Parameters.AddWithValue("next_attempt_at", NpgsqlDbType.TimestampTz,
                    exhausted ? now : now + RetryDelay(item.AttemptNumber));
                update.Parameters.AddWithValue("completed_at", NpgsqlDbType.TimestampTz,
                    exhausted ? (object)now : DBNull.Value);
                update.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, item.DeliveryId);
                await update.ExecuteNonQueryAsync(cancellationToken);
            }
            await AppendAttemptEventAsync(connection, transaction, item.DeliveryId, item.AttemptNumber,
                "LEASE_EXPIRED", null, "LEASE_EXPIRED", now, cancellationToken);
        }
    }

    private static async Task AppendAttemptEventAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string deliveryId,
        int attemptNumber,
        string action,
        int? httpStatus,
        string? errorCode,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO webhook_delivery_attempt_events (
                delivery_id, attempt_number, action, http_status, error_code, occurred_at)
            VALUES (@delivery_id, @attempt_number, @action, @http_status, @error_code, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, deliveryId);
        command.Parameters.AddWithValue("attempt_number", NpgsqlDbType.Integer, attemptNumber);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("http_status", NpgsqlDbType.Integer, (object?)httpStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("error_code", NpgsqlDbType.Text, (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static TimeSpan RetryDelay(int attemptNumber) => attemptNumber switch
    {
        1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        3 => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromMinutes(30)
    };

    private static string CreateDeliveryId(string eventId, string endpointId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(eventId + ":" + endpointId));
        return "whd_" + Convert.ToHexString(bytes.AsSpan(0, 16)).ToLowerInvariant();
    }
}
