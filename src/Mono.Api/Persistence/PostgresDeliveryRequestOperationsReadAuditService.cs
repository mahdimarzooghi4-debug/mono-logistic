using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Append-only accountability records for privileged Delivery Request reads.</summary>
public sealed class PostgresDeliveryRequestOperationsReadAuditService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    public const string Search = "DELIVERY_REQUEST_SEARCH";
    public const string Detail = "DELIVERY_REQUEST_DETAIL";

    public async Task RecordAsync(
        string action,
        string? deliveryRequestId,
        int resultCount,
        CancellationToken cancellationToken = default)
    {
        if (action is not (Search or Detail) || resultCount < 0 ||
            (action == Search && deliveryRequestId is not null) ||
            (action == Detail && (deliveryRequestId is null ||
             !Mono.Api.Domain.DeliveryRequestInputValidator.IsValidDeliveryRequestId(deliveryRequestId))))
            throw new ArgumentException("The Delivery Request read audit scope is invalid.");

        var actorId = operationsContext.ActorId;
        if (string.IsNullOrWhiteSpace(actorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            INSERT INTO delivery_request_operations_read_audit (
                audit_id, actor_id, action, delivery_request_id, result_count, occurred_at)
            VALUES (@audit_id, @actor_id, @action, @delivery_request_id, @result_count, @occurred_at);
            """, connection);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "dra_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text,
            (object?)deliveryRequestId ?? DBNull.Value);
        command.Parameters.AddWithValue("result_count", NpgsqlDbType.Integer, resultCount);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
