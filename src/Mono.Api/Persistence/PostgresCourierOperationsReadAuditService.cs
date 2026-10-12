using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Fail-closed, append-only accountability for privileged courier reads.</summary>
public sealed class PostgresCourierOperationsReadAuditService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    TimeProvider? timeProvider = null)
{
    public const string CourierSearch = "COURIER_SEARCH";
    public const string CourierDetail = "COURIER_DETAIL";
    public const string CourierAuditInspection = "COURIER_AUDIT_INSPECTION";
    public const string CourierAvailabilitySearch = "COURIER_AVAILABILITY_SEARCH";

    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AuditIdPattern = new(
        @"\Acra_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task RecordAsync(
        string action,
        string? courierId,
        int resultCount,
        CancellationToken cancellationToken = default)
    {
        if (action is not (CourierSearch or CourierDetail or CourierAuditInspection or CourierAvailabilitySearch) ||
            resultCount is < 0 or > 101 ||
            ((action is CourierSearch or CourierAuditInspection or CourierAvailabilitySearch) && courierId is not null) ||
            (action == CourierDetail &&
             (courierId is null || !CourierIdPattern.IsMatch(courierId))))
            throw new ArgumentException("Courier read audit scope is invalid.");

        var actorId = RequireAdministrator();
        const string sql = """
            INSERT INTO courier_operations_read_audit
                (audit_id, actor_id, action, courier_id, result_count, occurred_at)
            VALUES (@audit_id, @actor_id, @action, @courier_id, @result_count, @occurred_at);
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "cra_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, (object?)courierId ?? DBNull.Value);
        command.Parameters.AddWithValue("result_count", NpgsqlDbType.Integer, resultCount);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, _timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<CourierOperationsReadAuditPage?> ListAsync(
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        if (pageSize is < 1 or > 100 || (cursor is not null && !AuditIdPattern.IsMatch(cursor)))
            throw new ArgumentException("Courier read audit filters are invalid.");

        DateTimeOffset? cursorOccurredAt = null;
        if (cursor is not null)
        {
            await using var cursorConnection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var cursorCommand = new NpgsqlCommand("""
                SELECT occurred_at FROM courier_operations_read_audit WHERE audit_id = @audit_id;
                """, cursorConnection);
            cursorCommand.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, cursor);
            await using var cursorReader = await cursorCommand.ExecuteReaderAsync(cancellationToken);
            if (!await cursorReader.ReadAsync(cancellationToken))
                return null;
            cursorOccurredAt = cursorReader.GetFieldValue<DateTimeOffset>(0);
        }

        const string firstPageSql = """
            SELECT audit_id, actor_id, action, courier_id, result_count, occurred_at
            FROM courier_operations_read_audit
            ORDER BY occurred_at DESC, audit_id DESC
            LIMIT @take;
            """;
        const string nextPageSql = """
            SELECT audit_id, actor_id, action, courier_id, result_count, occurred_at
            FROM courier_operations_read_audit
            WHERE occurred_at < @cursor_occurred_at
               OR (occurred_at = @cursor_occurred_at AND audit_id < @cursor_id)
            ORDER BY occurred_at DESC, audit_id DESC
            LIMIT @take;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(cursor is null ? firstPageSql : nextPageSql, connection);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
        if (cursor is not null)
        {
            command.Parameters.AddWithValue("cursor_occurred_at", NpgsqlDbType.TimestampTz, cursorOccurredAt!.Value);
            command.Parameters.AddWithValue("cursor_id", NpgsqlDbType.Text, cursor);
        }

        var items = new List<CourierOperationsReadAuditItem>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new CourierOperationsReadAuditItem(
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

        await RecordAsync(CourierAuditInspection, null, items.Count, cancellationToken);
        return new CourierOperationsReadAuditPage(items, nextCursor);
    }

    public static bool IsValidAuditCursor(string? cursor) =>
        cursor is null || AuditIdPattern.IsMatch(cursor);

    private string RequireAdministrator()
    {
        var actorId = operationsContext.ActorId;
        if (string.IsNullOrWhiteSpace(actorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return actorId;
    }
}
