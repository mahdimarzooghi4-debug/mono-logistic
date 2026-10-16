using Mono.Api.Domain;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json.Serialization;

namespace Mono.Api.Persistence;

public sealed record WebhookDeliveryHistoryItem(
    [property: JsonPropertyName("delivery_id")] string DeliveryId,
    [property: JsonPropertyName("endpoint_id")] string EndpointId,
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("attempt_count")] int AttemptCount,
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

public sealed class PostgresWebhookDeliveryHistoryRepository(NpgsqlDataSource dataSource)
{
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
            SELECT delivery_id, endpoint_id, event_id, event_type, status, attempt_count,
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

    public async Task<WebhookDeliveryHistoryDetail?> FindAsync(
        string customerId,
        string deliveryId,
        CancellationToken cancellationToken)
    {
        const string deliverySql = """
            SELECT delivery_id, endpoint_id, event_id, event_type, status, attempt_count,
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
        reader.GetString(4), reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt32(6),
        reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetFieldValue<DateTimeOffset>(8),
        reader.IsDBNull(9) ? null : reader.GetFieldValue<DateTimeOffset>(9),
        reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
        reader.IsDBNull(11) ? null : reader.GetFieldValue<DateTimeOffset>(11));
}
