using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;
using System.Text.RegularExpressions;

namespace Mono.Api.Persistence;

/// <summary>Append-only accountability records and inspection for privileged Mission reads.</summary>
public sealed class PostgresMissionOperationsReadAuditService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    public const string MissionSearch = "MISSION_SEARCH";
    public const string MissionDetail = "MISSION_DETAIL";
    public const string MissionTimeline = "MISSION_TIMELINE";
    public const string MissionAuditInspection = "MISSION_AUDIT_INSPECTION";

    private static readonly Regex AuditIdPattern = new(
        @"\Amra_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task RecordAsync(
        string action,
        string? missionId,
        int resultCount,
        CancellationToken cancellationToken = default)
    {
        if (action is not (MissionSearch or MissionDetail or MissionTimeline or MissionAuditInspection))
            throw new ArgumentException("The Mission read audit action is invalid.", nameof(action));
        if (resultCount < 0 ||
            ((action is MissionSearch or MissionAuditInspection) && missionId is not null) ||
            ((action is MissionDetail or MissionTimeline) &&
             (missionId is null || !MissionHistoryInputValidator.IsValidMissionId(missionId))))
            throw new ArgumentException("The Mission read audit scope is invalid.");

        var actorId = RequireAdministrator();
        const string sql = """
            INSERT INTO mission_operations_read_audit (
                audit_id, actor_id, action, mission_id, result_count, occurred_at)
            VALUES (@audit_id, @actor_id, @action, @mission_id, @result_count, @occurred_at);
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "mra_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, (object?)missionId ?? DBNull.Value);
        command.Parameters.AddWithValue("result_count", NpgsqlDbType.Integer, resultCount);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MissionOperationsReadAuditPage?> ListAsync(
        int pageSize,
        string? missionId,
        string? actorId,
        DateTimeOffset? createdAfter,
        DateTimeOffset? createdBefore,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        if (pageSize is < 1 or > 100 ||
            (missionId is not null && !MissionHistoryInputValidator.IsValidMissionId(missionId)) ||
            !IsValidActorId(actorId) ||
            !MissionOperationsInputValidator.IsValidDateRange(createdAfter, createdBefore) ||
            !IsValidAuditCursor(cursor))
            throw new ArgumentException("Mission read audit filters are invalid.");

        DateTimeOffset? cursorOccurredAt = null;
        if (cursor is not null)
        {
            await using var cursorConnection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var cursorCommand = new NpgsqlCommand("""
                SELECT occurred_at, mission_id, actor_id
                FROM mission_operations_read_audit
                WHERE audit_id = @audit_id;
                """, cursorConnection);
            cursorCommand.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, cursor);
            await using var cursorReader = await cursorCommand.ExecuteReaderAsync(cancellationToken);
            if (!await cursorReader.ReadAsync(cancellationToken))
                return null;

            cursorOccurredAt = cursorReader.GetFieldValue<DateTimeOffset>(0);
            var cursorMissionId = cursorReader.IsDBNull(1) ? null : cursorReader.GetString(1);
            var cursorActorId = cursorReader.GetString(2);
            if ((missionId is not null && !string.Equals(missionId, cursorMissionId, StringComparison.Ordinal)) ||
                (actorId is not null && !string.Equals(actorId, cursorActorId, StringComparison.Ordinal)) ||
                (createdAfter is not null && cursorOccurredAt < createdAfter) ||
                (createdBefore is not null && cursorOccurredAt > createdBefore))
                return null;
        }

        const string firstPageSql = """
            SELECT audit_id, actor_id, action, mission_id, result_count, occurred_at
            FROM mission_operations_read_audit
            WHERE (@mission_id IS NULL OR mission_id = @mission_id)
              AND (@actor_id IS NULL OR actor_id = @actor_id)
              AND (@created_after IS NULL OR occurred_at >= @created_after)
              AND (@created_before IS NULL OR occurred_at <= @created_before)
            ORDER BY occurred_at DESC, audit_id DESC
            LIMIT @take;
            """;
        const string nextPageSql = """
            SELECT audit_id, actor_id, action, mission_id, result_count, occurred_at
            FROM mission_operations_read_audit
            WHERE (@mission_id IS NULL OR mission_id = @mission_id)
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
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, (object?)missionId ?? DBNull.Value);
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

        var items = new List<MissionOperationsReadAuditItem>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new MissionOperationsReadAuditItem(
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

        await RecordAsync(MissionAuditInspection, null, items.Count, cancellationToken);
        return new MissionOperationsReadAuditPage(items, nextCursor);
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
