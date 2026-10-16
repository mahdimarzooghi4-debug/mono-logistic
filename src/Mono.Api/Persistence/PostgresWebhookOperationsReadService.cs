using System.Text.Json.Serialization;
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

public sealed class PostgresWebhookOperationsReadService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
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

        const string auditSql = """
            INSERT INTO webhook_operations_read_audit (audit_id, actor_id, action, result_count, occurred_at)
            VALUES (@audit_id, @actor_id, 'QUEUE_SUMMARY', @result_count, @occurred_at);
            """;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(auditSql, connection))
        {
            command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "woa_" + Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            command.Parameters.AddWithValue("result_count", NpgsqlDbType.Bigint, summary.TotalCount);
            command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        return summary;
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
