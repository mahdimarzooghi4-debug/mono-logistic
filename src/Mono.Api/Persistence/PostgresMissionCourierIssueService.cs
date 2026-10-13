using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Records structured courier incidents and operations resolution as immutable events.</summary>
public sealed class PostgresMissionCourierIssueService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext)
{
    private static readonly Regex MissionIdPattern = new(@"\Amis_[0-9a-f]{32}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CourierIdPattern = new(@"\Acrr_[0-9a-f]{32}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ReportIdPattern = new(@"\Amci_[0-9a-f]{32}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex KeyPattern = new(@"\A[A-Za-z0-9._~-]{1,128}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> IssueCodes = new(StringComparer.Ordinal)
    {
        "ACCESS_BLOCKED", "RECIPIENT_UNAVAILABLE", "PACKAGE_MISMATCH", "SAFETY_CONCERN", "OTHER"
    };

    public async Task<MissionCourierIssueResult> ReportAsync(
        string courierId, string missionId, string issueCode, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Validate(CourierIdPattern, courierId, nameof(courierId));
        Validate(MissionIdPattern, missionId, nameof(missionId));
        Validate(KeyPattern, idempotencyKey, nameof(idempotencyKey));
        if (!IssueCodes.Contains(issueCode))
            throw new ArgumentException("Issue code is not supported.", nameof(issueCode));
        const string operation = "REPORT_ISSUE";
        var hash = Hash(JsonSerializer.Serialize(new { courier_id = courierId, mission_id = missionId, issue_code = issueCode }));
        var now = UtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockKeyAsync(connection, transaction, missionId, operation, idempotencyKey, cancellationToken);
        var replay = await FindReplayAsync(connection, transaction, missionId, operation, idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        const string assignmentSql = """
            SELECT mission.status, EXISTS (SELECT 1 FROM mission_courier_offers offer
                WHERE offer.mission_id = mission.mission_id AND offer.courier_id = @courier_id
                  AND offer.status = 'ACCEPTED')
            FROM missions mission WHERE mission.mission_id = @mission_id FOR UPDATE;
            """;
        string? status = null;
        var assigned = false;
        await using (var command = new NpgsqlCommand(assignmentSql, connection, transaction))
        {
            command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                status = reader.GetString(0);
                assigned = reader.GetBoolean(1);
            }
        }
        if (status is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierIssueResult(MissionCourierIssueOutcome.MissionNotFound, null);
        }
        if (!assigned || status is not ("COURIER_ASSIGNED" or "ARRIVED_PICKUP" or "HANDED_TO_COURIER" or
            "PICKED_UP" or "IN_TRANSIT" or "ARRIVED_DROPOFF"))
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierIssueResult(MissionCourierIssueOutcome.InvalidState, null);
        }

        var report = new MissionCourierIssueReport(
            "mci_" + Guid.NewGuid().ToString("N"), missionId, courierId, issueCode, "OPEN", now, now);
        await InsertEventAsync(connection, transaction, report, "REPORTED", "COURIER", courierId, now, cancellationToken);
        await SaveIdempotencyAsync(connection, transaction, missionId, operation, idempotencyKey, hash, report, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new MissionCourierIssueResult(MissionCourierIssueOutcome.Applied, report);
    }

    public async Task<MissionCourierIssueResult> ResolveAsync(
        string missionId, string reportId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        Validate(MissionIdPattern, missionId, nameof(missionId));
        Validate(ReportIdPattern, reportId, nameof(reportId));
        Validate(KeyPattern, idempotencyKey, nameof(idempotencyKey));
        const string operation = "RESOLVE_ISSUE";
        var hash = Hash(JsonSerializer.Serialize(new { mission_id = missionId, report_id = reportId, action = operation }));
        var now = UtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockKeyAsync(connection, transaction, reportId, operation, idempotencyKey, cancellationToken);
        var replay = await FindReplayAsync(connection, transaction, reportId, operation, idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }
        var report = await ReadReportAsync(connection, transaction, reportId, forUpdate: true, cancellationToken);
        if (report is null || !string.Equals(report.MissionId, missionId, StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierIssueResult(MissionCourierIssueOutcome.IssueNotFound, null);
        }
        if (report.Status != "OPEN")
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierIssueResult(MissionCourierIssueOutcome.InvalidState, report);
        }
        var resolved = report with { Status = "RESOLVED", UpdatedAt = now };
        await InsertEventAsync(connection, transaction, resolved, "RESOLVED", "OPERATIONS", actorId, now, cancellationToken);
        await SaveIdempotencyAsync(connection, transaction, reportId, operation, idempotencyKey, hash, resolved, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new MissionCourierIssueResult(MissionCourierIssueOutcome.Applied, resolved);
    }

    public async Task<IReadOnlyList<MissionCourierIssueReport>> ListForMissionAsync(
        string missionId, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        Validate(MissionIdPattern, missionId, nameof(missionId));
        const string sql = """
            SELECT report_id, mission_id, courier_id, issue_code, status, reported_at, updated_at
            FROM mission_courier_issue_reports WHERE mission_id = @mission_id
            ORDER BY reported_at DESC, report_id DESC LIMIT 100;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
        var reports = new List<MissionCourierIssueReport>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) reports.Add(ReadReport(reader));
        return reports;
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, MissionCourierIssueReport report,
        string action, string actorType, string actorId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO mission_courier_issue_events
                (event_id, report_id, mission_id, courier_id, issue_code, action, actor_type, actor_id, occurred_at)
            VALUES (@event_id, @report_id, @mission_id, @courier_id, @issue_code, @action, @actor_type, @actor_id, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, "mcie_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("report_id", NpgsqlDbType.Text, report.ReportId);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, report.MissionId);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, report.CourierId);
        command.Parameters.AddWithValue("issue_code", NpgsqlDbType.Text, report.IssueCode);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("actor_type", NpgsqlDbType.Text, actorType);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<MissionCourierIssueReport?> ReadReportAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string reportId, bool forUpdate,
        CancellationToken cancellationToken)
    {
        var sql = """
            SELECT report_id, mission_id, courier_id, issue_code, status, reported_at, updated_at
            FROM mission_courier_issue_reports WHERE report_id = @report_id
            """ + (forUpdate ? " FOR UPDATE;" : ";");
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("report_id", NpgsqlDbType.Text, reportId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadReport(reader) : null;
    }

    private static MissionCourierIssueReport ReadReport(NpgsqlDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
        reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6));

    private static async Task<MissionCourierIssueResult?> FindReplayAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string scope, string operation,
        string key, string hash, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT request_hash, response FROM mission_courier_issue_idempotency
            WHERE scope_id = @scope AND operation = @operation AND idempotency_key = @key;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("scope", NpgsqlDbType.Text, scope);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var storedHash = reader.GetString(0);
        var json = reader.GetString(1);
        await reader.CloseAsync();
        return string.Equals(storedHash, hash, StringComparison.Ordinal)
            ? new MissionCourierIssueResult(MissionCourierIssueOutcome.Replayed, JsonSerializer.Deserialize<MissionCourierIssueReport>(json))
            : new MissionCourierIssueResult(MissionCourierIssueOutcome.Conflict, null);
    }

    private static async Task SaveIdempotencyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string scope, string operation,
        string key, string hash, MissionCourierIssueReport report, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO mission_courier_issue_idempotency
                (scope_id, operation, idempotency_key, request_hash, response, created_at)
            VALUES (@scope, @operation, @key, @hash, @response::jsonb, @created_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("scope", NpgsqlDbType.Text, scope);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, key);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Text, hash);
        command.Parameters.AddWithValue("response", NpgsqlDbType.Text, JsonSerializer.Serialize(report));
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task LockKeyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string scope, string operation,
        string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0));", connection, transaction);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, $"{scope}:{operation}:{key}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return operationsContext.ActorId;
    }

    private static DateTimeOffset UtcNow()
    {
        var now = DateTimeOffset.UtcNow;
        return new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void Validate(Regex pattern, string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !pattern.IsMatch(value))
            throw new ArgumentException("Mission courier issue field is invalid.", name);
    }
}
