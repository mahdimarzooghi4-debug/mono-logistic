using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Append-only accountability records for privileged Mission reads.</summary>
public sealed class PostgresMissionOperationsReadAuditService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    public const string MissionSearch = "MISSION_SEARCH";
    public const string MissionDetail = "MISSION_DETAIL";
    public const string MissionTimeline = "MISSION_TIMELINE";

    public async Task RecordAsync(
        string action,
        string? missionId,
        int resultCount,
        CancellationToken cancellationToken = default)
    {
        if (action is not (MissionSearch or MissionDetail or MissionTimeline))
            throw new ArgumentException("The Mission read audit action is invalid.", nameof(action));
        if (resultCount < 0 || (action == MissionSearch && missionId is not null) ||
            (action != MissionSearch && (missionId is null ||
             !Mono.Api.Domain.MissionHistoryInputValidator.IsValidMissionId(missionId))))
            throw new ArgumentException("The Mission read audit scope is invalid.");

        var actorId = operationsContext.ActorId;
        if (string.IsNullOrWhiteSpace(actorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");

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
}
