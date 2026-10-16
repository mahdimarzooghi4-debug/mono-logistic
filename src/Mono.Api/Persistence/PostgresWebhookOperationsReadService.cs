using System.Text.Json.Serialization;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record WebhookOperationsQueueSummary(
    [property: JsonPropertyName("total_count")] long TotalCount,
    [property: JsonPropertyName("pending_count")] long PendingCount,
    [property: JsonPropertyName("in_progress_count")] long InProgressCount,
    [property: JsonPropertyName("retrying_count")] long RetryingCount,
    [property: JsonPropertyName("succeeded_count")] long SucceededCount,
    [property: JsonPropertyName("dead_lettered_count")] long DeadLetteredCount,
    [property: JsonPropertyName("cancelled_count")] long CancelledCount,
    [property: JsonPropertyName("due_count")] long DueCount,
    [property: JsonPropertyName("stale_lease_count")] long StaleLeaseCount,
    [property: JsonPropertyName("dead_lettered_last_24_hours")] long DeadLetteredLast24Hours,
    [property: JsonPropertyName("oldest_open_delivery_at")] DateTimeOffset? OldestOpenDeliveryAt,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt);

public sealed record WebhookOperationsAuditItem(
    [property: JsonPropertyName("audit_id")] string AuditId,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("result_count")] long ResultCount,
    [property: JsonPropertyName("resource_id")] string? ResourceId,
    [property: JsonPropertyName("outcome_code")] string? OutcomeCode,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record WebhookOperationsAuditPage(
    [property: JsonPropertyName("items")] IReadOnlyList<WebhookOperationsAuditItem> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public sealed class PostgresWebhookOperationsReadService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    PostgresWebhookDeliveryHistoryRepository deliveryHistory)
{
    public async Task<WebhookOperationsQueueSummary> GetQueueSummaryAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var actorId = RequireAdministrator();
        const string sql = """
            SELECT COUNT(*)::bigint,
                   COUNT(*) FILTER (WHERE status = 'PENDING')::bigint,
                   COUNT(*) FILTER (WHERE status = 'IN_PROGRESS')::bigint,
                   COUNT(*) FILTER (WHERE status = 'RETRYING')::bigint,
                   COUNT(*) FILTER (WHERE status = 'SUCCEEDED')::bigint,
                   COUNT(*) FILTER (WHERE status = 'DEAD_LETTERED')::bigint,
                   COUNT(*) FILTER (WHERE status = 'CANCELLED')::bigint,
                   COUNT(*) FILTER (WHERE status IN ('PENDING', 'RETRYING') AND next_attempt_at <= @now)::bigint,
                   COUNT(*) FILTER (WHERE status = 'IN_PROGRESS' AND lease_expires_at <= @now)::bigint,
                   COUNT(*) FILTER (WHERE status = 'DEAD_LETTERED' AND completed_at >= @since)::bigint,
                   MIN(created_at) FILTER (WHERE status IN ('PENDING', 'IN_PROGRESS', 'RETRYING'))
            FROM webhook_deliveries;
            """;
        WebhookOperationsQueueSummary summary;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            command.Parameters.AddWithValue("since", NpgsqlDbType.TimestampTz, now.AddHours(-24));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            summary = new WebhookOperationsQueueSummary(
                reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3),
                reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), reader.GetInt64(7),
                reader.GetInt64(8), reader.GetInt64(9),
                reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10), now);
        }

        await WriteAuditAsync(actorId, "QUEUE_SUMMARY", summary.TotalCount, null, now, cancellationToken);
        return summary;
    }

    public async Task<WebhookDeliveryHistoryPage> SearchDeliveriesAsync(
        int limit, string? customerId, string? endpointId, string? eventType, string? status,
        DateTimeOffset? beforeCreatedAt, string? beforeDeliveryId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var actorId = RequireAdministrator();
        var page = await deliveryHistory.ListForOperationsAsync(limit, customerId, endpointId, eventType, status,
            beforeCreatedAt, beforeDeliveryId, cancellationToken);
        await WriteAuditAsync(actorId, "QUEUE_SEARCH", page.Items.Count, null, now, cancellationToken);
        return page;
    }

    public async Task<WebhookDeliveryHistoryDetail?> GetDeliveryDetailAsync(
        string deliveryId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var actorId = RequireAdministrator();
        var detail = await deliveryHistory.FindForOperationsAsync(deliveryId, cancellationToken);
        await WriteAuditAsync(actorId, "DELIVERY_DETAIL", detail is null ? 0 : 1, deliveryId, now, cancellationToken);
        return detail;
    }

    public async Task<WebhookDeliveryReplayResult> ReplayDeliveryAsync(
        string deliveryId, string idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var actorId = RequireAdministrator();
        return await deliveryHistory.ReplayForOperationsAsync(deliveryId, idempotencyKey, actorId, now,
            cancellationToken);
    }

    public async Task<WebhookDeliveryCancellationResult> CancelDeliveryAsync(
        string deliveryId, string idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var actorId = RequireAdministrator();
        return await deliveryHistory.CancelForOperationsAsync(deliveryId, idempotencyKey, actorId, now,
            cancellationToken);
    }

    public async Task<WebhookOperationsAuditPage> ListAuditAsync(
        int limit, string? actorId, string? action, DateTimeOffset? beforeOccurredAt,
        string? beforeAuditId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var operationsActorId = RequireAdministrator();
        const string sql = """
            SELECT audit_id, actor_id, action, result_count, resource_id, outcome_code, occurred_at
            FROM webhook_operations_read_audit
            WHERE (@actor_id_filter IS NULL OR actor_id = @actor_id_filter)
              AND (@action_filter IS NULL OR action = @action_filter)
              AND (@before_occurred_at IS NULL OR
                   (occurred_at, audit_id) < (@before_occurred_at, @before_audit_id))
            ORDER BY occurred_at DESC, audit_id DESC
            LIMIT @limit;
            """;
        var items = new List<WebhookOperationsAuditItem>();
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("actor_id_filter", NpgsqlDbType.Text, (object?)actorId ?? DBNull.Value);
            command.Parameters.AddWithValue("action_filter", NpgsqlDbType.Text, (object?)action ?? DBNull.Value);
            command.Parameters.AddWithValue("before_occurred_at", NpgsqlDbType.TimestampTz,
                (object?)beforeOccurredAt ?? DBNull.Value);
            command.Parameters.AddWithValue("before_audit_id", NpgsqlDbType.Text,
                (object?)beforeAuditId ?? DBNull.Value);
            command.Parameters.AddWithValue("limit", NpgsqlDbType.Integer, limit + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new WebhookOperationsAuditItem(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetInt64(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)));
        }

        string? nextCursor = null;
        if (items.Count > limit)
        {
            items.RemoveAt(items.Count - 1);
            var last = items[^1];
            nextCursor = WebhookOperationsAuditCursor.Encode(last.OccurredAt, last.AuditId, actorId, action);
        }

        await WriteAuditAsync(operationsActorId, "AUDIT_HISTORY_READ", items.Count, null, now,
            cancellationToken);
        return new WebhookOperationsAuditPage(items, nextCursor);
    }

    private async Task WriteAuditAsync(string actorId, string action, long resultCount, string? resourceId,
        DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        const string auditSql = """
            INSERT INTO webhook_operations_read_audit
                (audit_id, actor_id, action, result_count, resource_id, outcome_code, occurred_at)
            VALUES (@audit_id, @actor_id, @action, @result_count, @resource_id, NULL, @occurred_at);
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(auditSql, connection);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "woa_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("result_count", NpgsqlDbType.Bigint, resultCount);
        command.Parameters.AddWithValue("resource_id", NpgsqlDbType.Text, (object?)resourceId ?? DBNull.Value);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string RequireAdministrator()
    {
        var actorId = operationsContext.ActorId;
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 128 ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("A verified Mono operations administrator is required.");
        return actorId;
    }
}
