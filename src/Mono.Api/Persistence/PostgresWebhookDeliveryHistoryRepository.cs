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

public sealed class PostgresWebhookDeliveryHistoryRepository(NpgsqlDataSource dataSource)
{
    public async Task<WebhookDeliveryReplayResult> ReplayAsync(
        string customerId,
        string originalDeliveryId,
        string idempotencyKey,
        DateTimeOffset now,
        CancellationToken cancellationToken)
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
            await transaction.RollbackAsync(cancellationToken);
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
        string eventId;
        string eventType;
        string status;
        string payload;
        await using (var command = new NpgsqlCommand(originalSql, connection, transaction))
        {
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            command.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, originalDeliveryId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new WebhookDeliveryReplayResult(null, "NOT_FOUND", false);
            }
            eventId = reader.GetString(0);
            eventType = reader.GetString(1);
            status = reader.GetString(2);
            payload = reader.GetString(3);
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
            await transaction.CommitAsync(cancellationToken);
            return new WebhookDeliveryReplayResult(
                (await FindAsync(customerId, priorReplayId, cancellationToken))?.Delivery, null, true);
        }

        if (status != "DEAD_LETTERED")
        {
            await transaction.RollbackAsync(cancellationToken);
            return new WebhookDeliveryReplayResult(null, "NOT_REPLAYABLE", false);
        }

        if (endpointStatus != "ACTIVE")
        {
            await transaction.RollbackAsync(cancellationToken);
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
            await transaction.RollbackAsync(cancellationToken);
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
        await transaction.CommitAsync(cancellationToken);
        return new WebhookDeliveryReplayResult(
            (await FindAsync(customerId, replayDeliveryId, cancellationToken))?.Delivery, null, false);
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
