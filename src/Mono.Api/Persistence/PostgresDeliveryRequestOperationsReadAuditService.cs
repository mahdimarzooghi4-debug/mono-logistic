using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Append-only accountability records and inspection for privileged Delivery Request reads.</summary>
public sealed class PostgresDeliveryRequestOperationsReadAuditService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    public const string Search = "DELIVERY_REQUEST_SEARCH";
    public const string Detail = "DELIVERY_REQUEST_DETAIL";
    public const string AuditInspection = "DELIVERY_REQUEST_AUDIT_INSPECTION";

    private static readonly Regex AuditIdPattern = new(
        @"\Adra_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task RecordAsync(
        string action,
        string? deliveryRequestId,
        int resultCount,
        CancellationToken cancellationToken = default)
    {
        if (action is not (Search or Detail or AuditInspection) ||
            resultCount < 0 ||
            ((action is Search or AuditInspection) && deliveryRequestId is not null) ||
            (action == Detail &&
             (deliveryRequestId is null ||
              !DeliveryRequestInputValidator.IsValidDeliveryRequestId(deliveryRequestId))))
            throw new ArgumentException("The Delivery Request read audit scope is invalid.");

        var actorId = RequireAdministrator();
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

    public async Task<DeliveryRequestOperationsReadAuditPage?> ListAsync(
        int pageSize,
        string? deliveryRequestId,
        string? actorId,
        DateTimeOffset? createdAfter,
        DateTimeOffset? createdBefore,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        if (pageSize is < 1 or > 100 ||
            (deliveryRequestId is not null &&
             !DeliveryRequestInputValidator.IsValidDeliveryRequestId(deliveryRequestId)) ||
            !IsValidActorId(actorId) ||
            !DeliveryRequestOperationsInputValidator.IsValidDateRange(createdAfter, createdBefore) ||
            !IsValidAuditCursor(cursor))
            throw new ArgumentException("Delivery Request read audit filters are invalid.");

        DateTimeOffset? cursorOccurredAt = null;
        if (cursor is not null)
        {
            await using var cursorConnection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var cursorCommand = new NpgsqlCommand("""
                SELECT occurred_at, delivery_request_id, actor_id
                FROM delivery_request_operations_read_audit
                WHERE audit_id = @audit_id;
                """, cursorConnection);
            cursorCommand.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, cursor);
            await using var cursorReader = await cursorCommand.ExecuteReaderAsync(cancellationToken);
            if (!await cursorReader.ReadAsync(cancellationToken))
                return null;

            cursorOccurredAt = cursorReader.GetFieldValue<DateTimeOffset>(0);
            var cursorRequestId = cursorReader.IsDBNull(1) ? null : cursorReader.GetString(1);
            var cursorActorId = cursorReader.GetString(2);
            if ((deliveryRequestId is not null &&
                 !string.Equals(deliveryRequestId, cursorRequestId, StringComparison.Ordinal)) ||
                (actorId is not null &&
                 !string.Equals(actorId, cursorActorId, StringComparison.Ordinal)) ||
                (createdAfter is not null && cursorOccurredAt < createdAfter) ||
                (createdBefore is not null && cursorOccurredAt > createdBefore))
                return null;
        }

        const string firstPageSql = """
            SELECT audit_id, actor_id, action, delivery_request_id, result_count, occurred_at
            FROM delivery_request_operations_read_audit
            WHERE (@delivery_request_id IS NULL OR delivery_request_id = @delivery_request_id)
              AND (@actor_id IS NULL OR actor_id = @actor_id)
              AND (@created_after IS NULL OR occurred_at >= @created_after)
              AND (@created_before IS NULL OR occurred_at <= @created_before)
            ORDER BY occurred_at DESC, audit_id DESC
            LIMIT @take;
            """;
        const string nextPageSql = """
            SELECT audit_id, actor_id, action, delivery_request_id, result_count, occurred_at
            FROM delivery_request_operations_read_audit
            WHERE (@delivery_request_id IS NULL OR delivery_request_id = @delivery_request_id)
              AND (@actor_id IS NULL OR actor_id = @actor_id)
              AND (@created_after IS NULL OR occurred_at >= @created_after)
              AND (@created_before IS NULL OR occurred_at <= @created_before)
              AND (occurred_at < @cursor_occurred_at
                   OR (occurred_at = @cursor_occurred_at AND audit_id < @cursor_id))
            ORDER BY occurred_at DESC, audit_id DESC
            LIMIT @take;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(cursor is null ? firstPageSql : nextPageSql, connection);
        command.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text,
            (object?)deliveryRequestId ?? DBNull.Value);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, (object?)actorId ?? DBNull.Value);
        command.Parameters.AddWithValue("created_after", NpgsqlDbType.TimestampTz,
            (object?)createdAfter ?? DBNull.Value);
        command.Parameters.AddWithValue("created_before", NpgsqlDbType.TimestampTz,
            (object?)createdBefore ?? DBNull.Value);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
        if (cursor is not null)
        {
            command.Parameters.AddWithValue("cursor_occurred_at", NpgsqlDbType.TimestampTz, cursorOccurredAt!.Value);
            command.Parameters.AddWithValue("cursor_id", NpgsqlDbType.Text, cursor);
        }

        var items = new List<DeliveryRequestOperationsReadAuditItem>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new DeliveryRequestOperationsReadAuditItem(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetFieldValue<DateTimeOffset>(5)));
            }
        }

        string? nextCursor = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            nextCursor = items[^1].AuditId;
        }

        await RecordAsync(AuditInspection, null, items.Count, cancellationToken);
        return new DeliveryRequestOperationsReadAuditPage(items, nextCursor);
    }

    public static bool IsValidAuditCursor(string? cursor) =>
        cursor is null || AuditIdPattern.IsMatch(cursor);

    public static bool IsValidActorId(string? actorId) =>
        actorId is null ||
        (actorId.Length is >= 1 and <= 256 &&
         string.Equals(actorId, actorId.Trim(), StringComparison.Ordinal) &&
         !actorId.Any(char.IsControl));

    private string RequireAdministrator()
    {
        var actorId = operationsContext.ActorId;
        if (string.IsNullOrWhiteSpace(actorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return actorId;
    }
}
