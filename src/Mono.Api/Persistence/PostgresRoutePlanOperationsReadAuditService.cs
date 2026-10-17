using System.Text.RegularExpressions;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record RoutePlanOperationsReadAuditItem(
    string AuditId, string ActorId, string? RouteId, string Action, int? Version,
    string Outcome, int ResultCount, DateTimeOffset OccurredAt);
public sealed record RoutePlanOperationsReadAuditCursor(DateTimeOffset OccurredAt, string AuditId);
public sealed record RoutePlanOperationsReadAuditPage(
    IReadOnlyList<RoutePlanOperationsReadAuditItem> Items,
    RoutePlanOperationsReadAuditCursor? NextCursor);

/// <summary>Append-only accountability for administrator Route plan reads.</summary>
public sealed class PostgresRoutePlanOperationsReadAuditService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    TimeProvider? timeProvider = null)
{
    public const string CurrentPlanRead = "ROUTE_PLAN_CURRENT_READ";
    public const string PlanVersionRead = "ROUTE_PLAN_VERSION_READ";
    public const string AuditInspection = "ROUTE_PLAN_AUDIT_INSPECTION";
    private static readonly Regex AuditIdPattern = new(
        @"\Arpa_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task RecordAsync(string action, string? routeId, int? version,
        string outcome, int resultCount, CancellationToken cancellationToken = default)
    {
        if (!IsAdministrator())
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        if (action is not (CurrentPlanRead or PlanVersionRead or AuditInspection) ||
            outcome is not ("FOUND" or "NOT_FOUND" or "EMPTY") ||
            resultCount is < 0 or > 100 ||
            (action == CurrentPlanRead &&
             (!IsRouteIdValid(routeId) || (version is not null and < 1) ||
              (outcome == "FOUND" && (version is null || resultCount != 1)) ||
              (outcome == "NOT_FOUND" && (version is not null || resultCount != 0)))) ||
            (action == PlanVersionRead &&
             (!IsRouteIdValid(routeId) || version is null or < 1 ||
              (outcome == "FOUND" && resultCount != 1) ||
              (outcome == "NOT_FOUND" && resultCount != 0))) ||
            (action == AuditInspection &&
             (routeId is not null || version is not null ||
              (outcome == "FOUND" && resultCount == 0) ||
              (outcome == "EMPTY" && resultCount != 0) || outcome == "NOT_FOUND")))
            throw new ArgumentException("Route plan read audit scope is invalid.");

        const string sql = "INSERT INTO route_plan_operations_read_audit " +
            "(audit_id, actor_id, route_id, action, version, outcome, result_count, occurred_at) " +
            "VALUES (@audit_id, @actor_id, @route_id, @action, @version, @outcome, @result_count, @occurred_at);";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("audit_id", NpgsqlDbType.Text, "rpa_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, operationsContext.ActorId!.Trim());
        command.Parameters.Add("route_id", NpgsqlDbType.Text).Value = (object?)routeId ?? DBNull.Value;
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.Add("version", NpgsqlDbType.Integer).Value = (object?)version ?? DBNull.Value;
        command.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, outcome);
        command.Parameters.AddWithValue("result_count", NpgsqlDbType.Integer, resultCount);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, _timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<RoutePlanOperationsReadAuditPage?> ListAsync(
        string? routeId, string? action, string? outcome,
        DateTimeOffset fromAt, DateTimeOffset toAt, int pageSize,
        RoutePlanOperationsReadAuditCursor? before,
        CancellationToken cancellationToken = default)
    {
        if (!IsAdministrator())
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        routeId = string.IsNullOrWhiteSpace(routeId) ? null : routeId.Trim();
        action = string.IsNullOrWhiteSpace(action) ? null : action.Trim().ToUpperInvariant();
        outcome = string.IsNullOrWhiteSpace(outcome) ? null : outcome.Trim().ToUpperInvariant();
        fromAt = fromAt.ToUniversalTime();
        toAt = toAt.ToUniversalTime();
        if ((routeId is not null && !IsRouteIdValid(routeId)) ||
            (action is not null && action is not (CurrentPlanRead or PlanVersionRead or AuditInspection)) ||
            (outcome is not null && outcome is not ("FOUND" or "NOT_FOUND" or "EMPTY")) ||
            fromAt >= toAt || pageSize is < 1 or > 100 ||
            (before is not null && !AuditIdPattern.IsMatch(before.AuditId)))
            throw new ArgumentException("Route plan read audit filters are invalid.");

        const string sql = "SELECT audit_id, actor_id, route_id, action, version, outcome, result_count, occurred_at " +
            "FROM route_plan_operations_read_audit " +
            "WHERE occurred_at >= @from_at AND occurred_at < @to_at " +
            "AND (@route_id IS NULL OR route_id = @route_id) " +
            "AND (@action IS NULL OR action = @action) " +
            "AND (@outcome IS NULL OR outcome = @outcome) " +
            "AND (@before_at IS NULL OR (occurred_at, audit_id) < (@before_at, @before_id)) " +
            "ORDER BY occurred_at DESC, audit_id DESC LIMIT @take;";
        var items = new List<RoutePlanOperationsReadAuditItem>(pageSize + 1);
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("from_at", NpgsqlDbType.TimestampTz, fromAt);
            command.Parameters.AddWithValue("to_at", NpgsqlDbType.TimestampTz, toAt);
            command.Parameters.Add("route_id", NpgsqlDbType.Text).Value = (object?)routeId ?? DBNull.Value;
            command.Parameters.Add("action", NpgsqlDbType.Text).Value = (object?)action ?? DBNull.Value;
            command.Parameters.Add("outcome", NpgsqlDbType.Text).Value = (object?)outcome ?? DBNull.Value;
            command.Parameters.Add("before_at", NpgsqlDbType.TimestampTz).Value =
                before is null ? DBNull.Value : before.OccurredAt.ToUniversalTime();
            command.Parameters.Add("before_id", NpgsqlDbType.Text).Value =
                before is null ? DBNull.Value : before.AuditId;
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new RoutePlanOperationsReadAuditItem(
                    reader.GetString(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.GetString(5), reader.GetInt32(6), reader.GetFieldValue<DateTimeOffset>(7)));
            }
        }

        RoutePlanOperationsReadAuditCursor? next = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            var last = items[^1];
            next = new RoutePlanOperationsReadAuditCursor(last.OccurredAt, last.AuditId);
        }
        await RecordAsync(AuditInspection, null, null,
            items.Count == 0 ? "EMPTY" : "FOUND", items.Count, cancellationToken);
        return new RoutePlanOperationsReadAuditPage(items, next);
    }

    public static bool IsValidAuditCursor(string? cursor) =>
        cursor is null || AuditIdPattern.IsMatch(cursor);

    public static bool IsRouteIdValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value == value.Trim() &&
        !value.Any(char.IsControl);

    private bool IsAdministrator() =>
        !string.IsNullOrWhiteSpace(operationsContext.ActorId) &&
        operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal);
}
