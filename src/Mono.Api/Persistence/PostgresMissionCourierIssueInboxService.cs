using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record MissionCourierIssueInboxCursor(DateTimeOffset ReportedAt, string ReportId);
public sealed record MissionCourierIssueInboxPage(
    IReadOnlyList<MissionCourierIssueReport> Reports, MissionCourierIssueInboxCursor? NextCursor);

/// <summary>Administrator-only, bounded search across courier incident reports.</summary>
public sealed class PostgresMissionCourierIssueInboxService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    PostgresMissionOperationsReadAuditService readAudit)
{
    private static readonly Regex MissionIdPattern = new(@"\Amis_[0-9a-f]{32}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CourierIdPattern = new(@"\Acrr_[0-9a-f]{32}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ReportIdPattern = new(@"\Amci_[0-9a-f]{32}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> IssueCodes = new(StringComparer.Ordinal)
    {
        "ACCESS_BLOCKED", "RECIPIENT_UNAVAILABLE", "PACKAGE_MISMATCH", "SAFETY_CONCERN", "OTHER"
    };

    public async Task<MissionCourierIssueInboxPage> SearchAsync(
        int pageSize, string? status, string? issueCode, string? missionId, string? courierId,
        DateTimeOffset? fromAt, DateTimeOffset? toAt, MissionCourierIssueInboxCursor? before,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        if (pageSize is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(pageSize));
        status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
        if (status is not (null or "OPEN" or "RESOLVED")) throw new ArgumentException("Issue status is invalid.", nameof(status));
        issueCode = string.IsNullOrWhiteSpace(issueCode) ? null : issueCode.Trim().ToUpperInvariant();
        if (issueCode is not null && !IssueCodes.Contains(issueCode)) throw new ArgumentException("Issue code is invalid.", nameof(issueCode));
        missionId = string.IsNullOrWhiteSpace(missionId) ? null : missionId.Trim();
        courierId = string.IsNullOrWhiteSpace(courierId) ? null : courierId.Trim();
        if (missionId is not null && !MissionIdPattern.IsMatch(missionId)) throw new ArgumentException("Mission ID is invalid.", nameof(missionId));
        if (courierId is not null && !CourierIdPattern.IsMatch(courierId)) throw new ArgumentException("Courier ID is invalid.", nameof(courierId));
        if (fromAt.HasValue) fromAt = fromAt.Value.ToUniversalTime();
        if (toAt.HasValue) toAt = toAt.Value.ToUniversalTime();
        if (fromAt.HasValue && toAt.HasValue && fromAt >= toAt) throw new ArgumentException("Issue time range is invalid.");
        if (before is not null && (!ReportIdPattern.IsMatch(before.ReportId) ||
            (fromAt.HasValue && before.ReportedAt < fromAt) || (toAt.HasValue && before.ReportedAt >= toAt)))
            throw new ArgumentException("Issue cursor is invalid.");

        const string firstSql = """
            SELECT report_id, mission_id, courier_id, issue_code, status, reported_at, updated_at
            FROM mission_courier_issue_reports
            WHERE (@status IS NULL OR status = @status)
              AND (@issue_code IS NULL OR issue_code = @issue_code)
              AND (@mission_id IS NULL OR mission_id = @mission_id)
              AND (@courier_id IS NULL OR courier_id = @courier_id)
              AND (@from_at IS NULL OR reported_at >= @from_at)
              AND (@to_at IS NULL OR reported_at < @to_at)
            ORDER BY reported_at DESC, report_id DESC LIMIT @take;
            """;
        const string nextSql = """
            SELECT report_id, mission_id, courier_id, issue_code, status, reported_at, updated_at
            FROM mission_courier_issue_reports
            WHERE (@status IS NULL OR status = @status)
              AND (@issue_code IS NULL OR issue_code = @issue_code)
              AND (@mission_id IS NULL OR mission_id = @mission_id)
              AND (@courier_id IS NULL OR courier_id = @courier_id)
              AND (@from_at IS NULL OR reported_at >= @from_at)
              AND (@to_at IS NULL OR reported_at < @to_at)
              AND (reported_at < @cursor_at OR (reported_at = @cursor_at AND report_id < @cursor_id))
            ORDER BY reported_at DESC, report_id DESC LIMIT @take;
            """;
        var reports = new List<MissionCourierIssueReport>(pageSize + 1);
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(before is null ? firstSql : nextSql, connection))
        {
            command.Parameters.Add("status", NpgsqlDbType.Text).Value = status is null ? DBNull.Value : status;
            command.Parameters.Add("issue_code", NpgsqlDbType.Text).Value = issueCode is null ? DBNull.Value : issueCode;
            command.Parameters.Add("mission_id", NpgsqlDbType.Text).Value = missionId is null ? DBNull.Value : missionId;
            command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value = courierId is null ? DBNull.Value : courierId;
            command.Parameters.Add("from_at", NpgsqlDbType.TimestampTz).Value = fromAt.HasValue ? fromAt.Value : DBNull.Value;
            command.Parameters.Add("to_at", NpgsqlDbType.TimestampTz).Value = toAt.HasValue ? toAt.Value : DBNull.Value;
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
            if (before is not null)
            {
                command.Parameters.AddWithValue("cursor_at", NpgsqlDbType.TimestampTz, before.ReportedAt);
                command.Parameters.AddWithValue("cursor_id", NpgsqlDbType.Text, before.ReportId);
            }
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                reports.Add(new MissionCourierIssueReport(reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3), reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6)));
        }
        MissionCourierIssueInboxCursor? next = null;
        if (reports.Count > pageSize)
        {
            reports.RemoveAt(reports.Count - 1);
            var last = reports[^1];
            next = new MissionCourierIssueInboxCursor(last.ReportedAt, last.ReportId);
        }
        await readAudit.RecordAsync(PostgresMissionOperationsReadAuditService.MissionCourierIssueInboxSearch,
            null, reports.Count, cancellationToken);
        return new MissionCourierIssueInboxPage(reports, next);
    }
}
