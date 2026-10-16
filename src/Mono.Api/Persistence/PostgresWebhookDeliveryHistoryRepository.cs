using Mono.Api.Domain;
using Npgsql;
using NpgsqlTypes;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Mono.Api.Persistence;

public sealed record WebhookDeliveryHistoryItem(
    [property: JsonPropertyName("delivery_id")] string DeliveryId,
    [property: JsonPropertyName("endpoint_id")] string EndpointId,
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("attempt_count")] int AttemptCount,
    [property: JsonPropertyName("replay_generation")] int ReplayGeneration,
    [property: JsonPropertyName("last_http_status")] int? LastHttpStatus,
    [property: JsonPropertyName("last_error_code")] string? LastErrorCode,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("last_attempt_at")] DateTimeOffset? LastAttemptAt,
    [property: JsonPropertyName("next_attempt_at")] DateTimeOffset? NextAttemptAt,
    [property: JsonPropertyName("completed_at")] DateTimeOffset? CompletedAt);

public sealed record WebhookDeliveryAttemptEvent(
    [property: JsonPropertyName("attempt_number")] int AttemptNumber,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("http_status")] int? HttpStatus,
    [property: JsonPropertyName("error_code")] string? ErrorCode,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record WebhookDeliveryHistoryDetail(
    WebhookDeliveryHistoryItem Delivery,
    IReadOnlyList<WebhookDeliveryAttemptEvent> Attempts);

public sealed record WebhookDeliveryHistoryPage(
    IReadOnlyList<WebhookDeliveryHistoryItem> Items,
    string? NextCursor);

public sealed record WebhookDeliveryReplayResult(
    WebhookDeliveryHistoryItem? Delivery,
    string? ErrorCode,
    bool AlreadyRequested);

public sealed record WebhookDeliveryCancellationResult(string OutcomeCode, bool AlreadyRequested);

public sealed class PostgresWebhookDeliveryHistoryRepository(NpgsqlDataSource dataSource)
{
    public async Task<WebhookDeliveryCancellationResult> CancelForOperationsAsync(
        string deliveryId,
        string idempotencyKey,
        string operationsActorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string lockSql = """
            SELECT status FROM webhook_deliveries WHERE delivery_id = @delivery_id FOR UPDATE;
            """;
        string? status;
        await using (var command = new NpgsqlCommand(lockSql, connection, transaction))
        {
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, deliveryId);
            status = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }

        const string priorSql = """
            SELECT outcome_code FROM webhook_delivery_cancellation_requests
            WHERE delivery_id = @delivery_id AND idempotency_key = @idempotency_key;
            """;
        string? priorOutcome;
        await using (var command = new NpgsqlCommand(priorSql, connection, transaction))
        {
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, deliveryId);
            command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            priorOutcome = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }
        if (priorOutcome is not null)
        {
            await InsertOperationsCancellationAuditAsync(connection, transaction, operationsActorId,
                deliveryId, "ALREADY_REQUESTED", 1, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new WebhookDeliveryCancellationResult(priorOutcome, true);
        }

        var outcome = status switch
        {
            null => "NOT_FOUND",
            "PENDING" or "RETRYING" => "CANCELLED",
            "CANCELLED" => "ALREADY_CANCELLED",
            _ => "NOT_CANCELLABLE"
        };
        if (outcome == "CANCELLED")
        {
            const string cancelSql = """
                UPDATE webhook_deliveries
                SET status = 'CANCELLED', completed_at = @now, lease_expires_at = NULL
                WHERE delivery_id = @delivery_id AND status IN ('PENDING', 'RETRYING')
                  AND lease_expires_at IS NULL;
                """;
            await using var command = new NpgsqlCommand(cancelSql, connection, transaction);
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, deliveryId);
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                outcome = "NOT_CANCELLABLE";
        }

        const string requestSql = """
            INSERT INTO webhook_delivery_cancellation_requests
                (delivery_id, idempotency_key, outcome_code, created_at)
            VALUES (@delivery_id, @idempotency_key, @outcome_code, @created_at);
            """;
        await using (var command = new NpgsqlCommand(requestSql, connection, transaction))
        {
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, deliveryId);
            command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            command.Parameters.AddWithValue("outcome_code", NpgsqlDbType.Text, outcome);
            command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertOperationsCancellationAuditAsync(connection, transaction, operationsActorId,
            deliveryId, outcome, outcome is "CANCELLED" or "ALREADY_CANCELLED" ? 1 : 0, now,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WebhookDeliveryCancellationResult(outcome, false);
    }

    private static async Task InsertOperationsCancellationAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string actorId,
        string deliveryId,
        string outcomeCode,
        long resultCount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO webhook_operations_read_audit
                (audit_id, actor_id, action, result_count, resource_id, outcome_code, occurred_at)
            VALUES (@audit_id, @actor_id, 'DELIVERY_CANCEL', @result_count, @resource_id, @outcome_code, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "woa_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("result_count", NpgsqlDbType.Bigint, resultCount);
        command.Parameters.AddWithValue("resource_id", NpgsqlDbType.Text, deliveryId);
        command.Parameters.AddWithValue("outcome_code", NpgsqlDbType.Text, outcomeCode);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WebhookDeliveryReplayResult> ReplayAsync(
        string customerId,
        string originalDeliveryId,
        string idempotencyKey,
        DateTimeOffset now,
        CancellationToken cancellationToken,
        string? operationsActorId = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        const string endpointLookupSql = """
            SELECT endpoint_id FROM webhook_deliveries
            WHERE customer_id = @customer_id AND delivery_id = @delivery_id;
            """;
        string? endpointId;
        await using (var command = new NpgsqlCommand(endpointLookupSql, connection, transaction))
        {
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, originalDeliveryId);
            endpointId = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }
        if (endpointId is null)
        {
            await FinishReplayAuditAsync(connection, transaction, operationsActorId, originalDeliveryId,
                "NOT_FOUND", 0, now, cancellationToken);
            return new WebhookDeliveryReplayResult(null, "NOT_FOUND", false);
        }

        const string endpointSql = """
            SELECT status FROM webhook_endpoints
            WHERE customer_id = @customer_id AND endpoint_id = @endpoint_id
            FOR UPDATE;
            """;
        string? endpointStatus;
        await using (var command = new NpgsqlCommand(endpointSql, connection, transaction))
        {
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
            endpointStatus = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }

        const string originalSql = """
            SELECT event_id, event_type, status, payload
            FROM webhook_deliveries
            WHERE customer_id = @customer_id AND delivery_id = @delivery_id
            FOR UPDATE;
            """;
        var eventId = string.Empty;
        var eventType = string.Empty;
        var status = string.Empty;
        var payload = string.Empty;
        bool originalFound;
        await using (var command = new NpgsqlCommand(originalSql, connection, transaction))
        {
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, originalDeliveryId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            originalFound = await reader.ReadAsync(cancellationToken);
            if (originalFound)
            {
                eventId = reader.GetString(0);
                eventType = reader.GetString(1);
                status = reader.GetString(2);
                payload = reader.GetString(3);
            }
        }
        if (!originalFound)
        {
            await FinishReplayAuditAsync(connection, transaction, operationsActorId, originalDeliveryId,
                "NOT_FOUND", 0, now, cancellationToken);
            return new WebhookDeliveryReplayResult(null, "NOT_FOUND", false);
        }

        const string priorRequestSql = """
            SELECT replay_delivery_id
            FROM webhook_delivery_replay_requests
            WHERE original_delivery_id = @original_delivery_id AND idempotency_key = @idempotency_key;
            """;
        string? priorReplayId;
        await using (var command = new NpgsqlCommand(priorRequestSql, connection, transaction))
        {
            command.Parameters.AddWithValue("original_delivery_id", NpgsqlDbType.Text, originalDeliveryId);
            command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            priorReplayId = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }
        if (priorReplayId is not null)
        {
            await InsertOperationsReplayAuditAsync(connection, transaction, operationsActorId, originalDeliveryId,
                "ALREADY_REQUESTED", 1, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new WebhookDeliveryReplayResult(
                (await FindAsync(customerId, priorReplayId, cancellationToken))?.Delivery, null, true);
        }

        if (status != "DEAD_LETTERED")
        {
            await FinishReplayAuditAsync(connection, transaction, operationsActorId, originalDeliveryId,
                "NOT_REPLAYABLE", 0, now, cancellationToken);
            return new WebhookDeliveryReplayResult(null, "NOT_REPLAYABLE", false);
        }

        if (endpointStatus != "ACTIVE")
        {
            await FinishReplayAuditAsync(connection, transaction, operationsActorId, originalDeliveryId,
                "ENDPOINT_DISABLED", 0, now, cancellationToken);
            return new WebhookDeliveryReplayResult(null, "ENDPOINT_DISABLED", false);
        }

        const string generationSql = """
            SELECT COALESCE(MAX(replay_generation), 0)
            FROM webhook_deliveries
            WHERE customer_id = @customer_id AND endpoint_id = @endpoint_id AND event_id = @event_id;
            """;
        int generation;
        await using (var command = new NpgsqlCommand(generationSql, connection, transaction))
        {
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
            command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            generation = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) + 1;
        }
        if (generation > 3)
        {
            await FinishReplayAuditAsync(connection, transaction, operationsActorId, originalDeliveryId,
                "REPLAY_LIMIT_REACHED", 0, now, cancellationToken);
            return new WebhookDeliveryReplayResult(null, "REPLAY_LIMIT_REACHED", false);
        }

        var replayDeliveryId = CreateReplayDeliveryId(eventId, endpointId, generation);
        const string insertDeliverySql = """
            INSERT INTO webhook_deliveries (
                delivery_id, endpoint_id, customer_id, event_id, event_type, payload, status,
                attempt_count, next_attempt_at, created_at, replay_generation)
            VALUES (@delivery_id, @endpoint_id, @customer_id, @event_id, @event_type, @payload,
                'PENDING', 0, @now, @now, @generation);
            """;
        await using (var command = new NpgsqlCommand(insertDeliverySql, connection, transaction))
        {
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, replayDeliveryId);
            command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            command.Parameters.AddWithValue("event_type", NpgsqlDbType.Text, eventType);
            command.Parameters.AddWithValue("payload", NpgsqlDbType.Jsonb, payload);
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            command.Parameters.AddWithValue("generation", NpgsqlDbType.Integer, generation);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertRequestSql = """
            INSERT INTO webhook_delivery_replay_requests (
                original_delivery_id, idempotency_key, replay_delivery_id, created_at)
            VALUES (@original_delivery_id, @idempotency_key, @replay_delivery_id, @created_at);
            """;
        await using (var command = new NpgsqlCommand(insertRequestSql, connection, transaction))
        {
            command.Parameters.AddWithValue("original_delivery_id", NpgsqlDbType.Text, originalDeliveryId);
            command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            command.Parameters.AddWithValue("replay_delivery_id", NpgsqlDbType.Text, replayDeliveryId);
            command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertOperationsReplayAuditAsync(connection, transaction, operationsActorId, originalDeliveryId,
            "QUEUED", 1, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new WebhookDeliveryReplayResult(
            (await FindAsync(customerId, replayDeliveryId, cancellationToken))?.Delivery, null, false);
    }

    public async Task<WebhookDeliveryReplayResult> ReplayForOperationsAsync(
        string originalDeliveryId,
        string idempotencyKey,
        string operationsActorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        const string customerSql = "SELECT customer_id FROM webhook_deliveries WHERE delivery_id = @delivery_id;";
        string? customerId;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(customerSql, connection))
        {
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, originalDeliveryId);
            customerId = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }
        if (customerId is null)
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await InsertOperationsReplayAuditAsync(connection, transaction, operationsActorId, originalDeliveryId,
                "NOT_FOUND", 0, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new WebhookDeliveryReplayResult(null, "NOT_FOUND", false);
        }

        return await ReplayAsync(customerId, originalDeliveryId, idempotencyKey, now, cancellationToken,
            operationsActorId);
    }

    private static async Task FinishReplayAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string? actorId,
        string deliveryId,
        string outcomeCode,
        long resultCount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (actorId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }
        await InsertOperationsReplayAuditAsync(connection, transaction, actorId, deliveryId, outcomeCode,
            resultCount, occurredAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertOperationsReplayAuditAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string? actorId,
        string deliveryId,
        string outcomeCode,
        long resultCount,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        if (actorId is null)
            return;
        const string sql = """
            INSERT INTO webhook_operations_read_audit
                (audit_id, actor_id, action, result_count, resource_id, outcome_code, occurred_at)
            VALUES (@audit_id, @actor_id, 'DELIVERY_REPLAY', @result_count, @resource_id, @outcome_code, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "woa_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("result_count", NpgsqlDbType.Bigint, resultCount);
        command.Parameters.AddWithValue("resource_id", NpgsqlDbType.Text, deliveryId);
        command.Parameters.AddWithValue("outcome_code", NpgsqlDbType.Text, outcomeCode);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<WebhookDeliveryHistoryPage> ListAsync(
        string customerId,
        int limit,
        string? endpointId,
        string? eventType,
        string? status,
        DateTimeOffset? beforeCreatedAt,
        string? beforeDeliveryId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT delivery_id, endpoint_id, event_id, event_type, status, attempt_count, replay_generation,
                   last_http_status, last_error_code, created_at, last_attempt_at,
                   next_attempt_at, completed_at
            FROM webhook_deliveries
            WHERE customer_id = @customer_id
              AND (@endpoint_id IS NULL OR endpoint_id = @endpoint_id)
              AND (@event_type IS NULL OR event_type = @event_type)
              AND (@status IS NULL OR status = @status)
              AND (@before_created_at IS NULL OR (created_at, delivery_id) < (@before_created_at, @before_delivery_id))
            ORDER BY created_at DESC, delivery_id DESC
            LIMIT @limit;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, (object?)endpointId ?? DBNull.Value);
        command.Parameters.AddWithValue("event_type", NpgsqlDbType.Text, (object?)eventType ?? DBNull.Value);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("before_created_at", NpgsqlDbType.TimestampTz, (object?)beforeCreatedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("before_delivery_id", NpgsqlDbType.Text, (object?)beforeDeliveryId ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit + 1);

        var items = new List<WebhookDeliveryHistoryItem>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                items.Add(ReadItem(reader));
        }

        string? nextCursor = null;
        if (items.Count > limit)
        {
            items.RemoveAt(items.Count - 1);
            var last = items[^1];
            nextCursor = WebhookDeliveryHistoryCursor.Encode(last.CreatedAt, last.DeliveryId,
                endpointId, eventType, status);
        }
        return new WebhookDeliveryHistoryPage(items, nextCursor);
    }

    public async Task<WebhookDeliveryHistoryPage> ListForOperationsAsync(
        int limit,
        string? customerId,
        string? endpointId,
        string? eventType,
        string? status,
        DateTimeOffset? beforeCreatedAt,
        string? beforeDeliveryId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT delivery_id, endpoint_id, event_id, event_type, status, attempt_count, replay_generation,
                   last_http_status, last_error_code, created_at, last_attempt_at,
                   next_attempt_at, completed_at
            FROM webhook_deliveries
            WHERE (@customer_id IS NULL OR customer_id = @customer_id)
              AND (@endpoint_id IS NULL OR endpoint_id = @endpoint_id)
              AND (@event_type IS NULL OR event_type = @event_type)
              AND (@status IS NULL OR status = @status)
              AND (@before_created_at IS NULL OR (created_at, delivery_id) < (@before_created_at, @before_delivery_id))
            ORDER BY created_at DESC, delivery_id DESC
            LIMIT @limit;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, (object?)customerId ?? DBNull.Value);
        command.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, (object?)endpointId ?? DBNull.Value);
        command.Parameters.AddWithValue("event_type", NpgsqlDbType.Text, (object?)eventType ?? DBNull.Value);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, (object?)status ?? DBNull.Value);
        command.Parameters.AddWithValue("before_created_at", NpgsqlDbType.TimestampTz, (object?)beforeCreatedAt ?? DBNull.Value);
        command.Parameters.AddWithValue("before_delivery_id", NpgsqlDbType.Text, (object?)beforeDeliveryId ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit + 1);

        var items = new List<WebhookDeliveryHistoryItem>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                items.Add(ReadItem(reader));
        }

        string? nextCursor = null;
        if (items.Count > limit)
        {
            items.RemoveAt(items.Count - 1);
            var last = items[^1];
            nextCursor = WebhookDeliveryHistoryCursor.Encode(last.CreatedAt, last.DeliveryId,
                endpointId, eventType, status, customerId);
        }
        return new WebhookDeliveryHistoryPage(items, nextCursor);
    }

    public async Task<WebhookDeliveryHistoryDetail?> FindForOperationsAsync(
        string deliveryId,
        CancellationToken cancellationToken)
    {
        const string deliverySql = """
            SELECT delivery_id, endpoint_id, event_id, event_type, status, attempt_count, replay_generation,
                   last_http_status, last_error_code, created_at, last_attempt_at,
                   next_attempt_at, completed_at
            FROM webhook_deliveries
            WHERE delivery_id = @delivery_id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        WebhookDeliveryHistoryItem? item;
        await using (var command = new NpgsqlCommand(deliverySql, connection))
        {
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, deliveryId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            item = await reader.ReadAsync(cancellationToken) ? ReadItem(reader) : null;
        }
        if (item is null)
            return null;

        const string attemptsSql = """
            SELECT attempt_number, action, http_status, error_code, occurred_at
            FROM webhook_delivery_attempt_events
            WHERE delivery_id = @delivery_id
            ORDER BY attempt_event_id;
            """;
        var attempts = new List<WebhookDeliveryAttemptEvent>();
        await using (var command = new NpgsqlCommand(attemptsSql, connection))
        {
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, deliveryId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                attempts.Add(new WebhookDeliveryAttemptEvent(reader.GetInt32(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4)));
        }
        return new WebhookDeliveryHistoryDetail(item, attempts);
    }

    public async Task<WebhookDeliveryHistoryDetail?> FindAsync(
        string customerId,
        string deliveryId,
        CancellationToken cancellationToken)
    {
        const string deliverySql = """
            SELECT delivery_id, endpoint_id, event_id, event_type, status, attempt_count, replay_generation,
                   last_http_status, last_error_code, created_at, last_attempt_at,
                   next_attempt_at, completed_at
            FROM webhook_deliveries
            WHERE customer_id = @customer_id AND delivery_id = @delivery_id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        WebhookDeliveryHistoryItem? item;
        await using (var command = new NpgsqlCommand(deliverySql, connection))
        {
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, deliveryId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            item = await reader.ReadAsync(cancellationToken) ? ReadItem(reader) : null;
        }
        if (item is null)
            return null;

        const string attemptsSql = """
            SELECT attempt_number, action, http_status, error_code, occurred_at
            FROM webhook_delivery_attempt_events
            WHERE delivery_id = @delivery_id
            ORDER BY attempt_event_id;
            """;
        var attempts = new List<WebhookDeliveryAttemptEvent>();
        await using (var command = new NpgsqlCommand(attemptsSql, connection))
        {
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, deliveryId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                attempts.Add(new WebhookDeliveryAttemptEvent(
                    reader.GetInt32(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetInt32(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4)));
            }
        }
        return new WebhookDeliveryHistoryDetail(item, attempts);
    }

    private static WebhookDeliveryHistoryItem ReadItem(NpgsqlDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
        reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6), reader.IsDBNull(7) ? null : reader.GetInt32(7),
        reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetFieldValue<DateTimeOffset>(9),
        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
        reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11),
        reader.IsDBNull(12) ? null : reader.GetFieldValue<DateTimeOffset>(12));

    private static string CreateReplayDeliveryId(string eventId, string endpointId, int generation)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{eventId}:{endpointId}:replay:{generation}"));
        return "whd_" + Convert.ToHexString(bytes.AsSpan(0, 16)).ToLowerInvariant();
    }
}
