using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierPayoutReconciliationAuditCursor(DateTimeOffset OccurredAt, string ReadAuditId);
public sealed record CourierPayoutReconciliationAuditItem(
    string ReadAuditId, string ActorId, string? CourierIdFilter, string? StatusFilter,
    string? ReconciliationCodeFilter, DateTimeOffset CreatedFrom, DateTimeOffset CreatedTo,
    bool OnlyDiscrepancies, int PageSize, int ResultCount, string OutcomeCode,
    DateTimeOffset OccurredAt);
public sealed record CourierPayoutReconciliationAuditPage(
    IReadOnlyList<CourierPayoutReconciliationAuditItem> Items,
    CourierPayoutReconciliationAuditCursor? NextCursor);

public sealed class PostgresCourierPayoutReconciliationAuditSearchService(
    NpgsqlDataSource dataSource, IMonoOperationsContext operationsContext)
{
    private static readonly string[] Statuses = ["REQUESTED", "PROCESSING", "PAID", "REJECTED"];
    private static readonly string[] ReconciliationCodes =
        ["MATCHED", "MISSING_PAYOUT", "UNEXPECTED_PAYOUT", "MULTIPLE_PAYOUTS", "LEDGER_MISMATCH"];
    private static readonly string[] Outcomes = ["FOUND", "EMPTY"];

    public async Task<CourierPayoutReconciliationAuditPage> ReadAsync(
        int pageSize, string? actorFilter, string? courierIdFilter, string? statusFilter,
        string? reconciliationCodeFilter, string? outcomeFilter, bool? onlyDiscrepanciesFilter,
        DateTimeOffset fromAt, DateTimeOffset toAt, CourierPayoutReconciliationAuditCursor? before,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Administrator role is required.");
        if (pageSize is < 1 or > 100 || fromAt >= toAt)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        actorFilter = string.IsNullOrWhiteSpace(actorFilter) ? null : actorFilter.Trim();
        if (actorFilter is { Length: > 128 })
            throw new ArgumentException("Actor filter is too long.", nameof(actorFilter));
        if (courierIdFilter is not null && !IsCourierId(courierIdFilter))
            throw new ArgumentException("A valid courier ID is required.", nameof(courierIdFilter));
        if (statusFilter is not null && !Statuses.Contains(statusFilter, StringComparer.Ordinal))
            throw new ArgumentException("Unknown withdrawal status.", nameof(statusFilter));
        if (reconciliationCodeFilter is not null &&
            !ReconciliationCodes.Contains(reconciliationCodeFilter, StringComparer.Ordinal))
            throw new ArgumentException("Unknown reconciliation code.", nameof(reconciliationCodeFilter));
        if (outcomeFilter is not null && !Outcomes.Contains(outcomeFilter, StringComparer.Ordinal))
            throw new ArgumentException("Unknown outcome.", nameof(outcomeFilter));

        const string sql = """
            SELECT read_audit_id, actor_id, courier_id_filter, status_filter,
                   reconciliation_code_filter, created_from_filter, created_to_filter,
                   only_discrepancies, page_size, result_count, outcome_code, occurred_at
            FROM courier_payout_reconciliation_read_audit
            WHERE occurred_at >= @from_at AND occurred_at < @to_at
              AND (@actor_filter IS NULL OR actor_id = @actor_filter)
              AND (@courier_filter IS NULL OR courier_id_filter = @courier_filter)
              AND (@status_filter IS NULL OR status_filter = @status_filter)
              AND (@code_filter IS NULL OR reconciliation_code_filter = @code_filter)
              AND (@outcome_filter IS NULL OR outcome_code = @outcome_filter)
              AND (@only_filter IS NULL OR only_discrepancies = @only_filter)
              AND (@before_at IS NULL OR (occurred_at, read_audit_id) < (@before_at, @before_id))
            ORDER BY occurred_at DESC, read_audit_id DESC
            LIMIT @take;
            """;
        var items = new List<CourierPayoutReconciliationAuditItem>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("from_at", NpgsqlDbType.TimestampTz, fromAt.ToUniversalTime());
            command.Parameters.AddWithValue("to_at", NpgsqlDbType.TimestampTz, toAt.ToUniversalTime());
            command.Parameters.Add("actor_filter", NpgsqlDbType.Text).Value = actorFilter is null ? DBNull.Value : actorFilter;
            command.Parameters.Add("courier_filter", NpgsqlDbType.Text).Value = courierIdFilter is null ? DBNull.Value : courierIdFilter;
            command.Parameters.Add("status_filter", NpgsqlDbType.Text).Value = statusFilter is null ? DBNull.Value : statusFilter;
            command.Parameters.Add("code_filter", NpgsqlDbType.Text).Value = reconciliationCodeFilter is null ? DBNull.Value : reconciliationCodeFilter;
            command.Parameters.Add("outcome_filter", NpgsqlDbType.Text).Value = outcomeFilter is null ? DBNull.Value : outcomeFilter;
            command.Parameters.Add("only_filter", NpgsqlDbType.Boolean).Value =
                onlyDiscrepanciesFilter is null ? DBNull.Value : onlyDiscrepanciesFilter;
            command.Parameters.Add("before_at", NpgsqlDbType.TimestampTz).Value =
                before is null ? DBNull.Value : before.OccurredAt.ToUniversalTime();
            command.Parameters.Add("before_id", NpgsqlDbType.Text).Value =
                before is null ? DBNull.Value : before.ReadAuditId;
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new CourierPayoutReconciliationAuditItem(
                    reader.GetString(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetBoolean(7), reader.GetInt32(8), reader.GetInt32(9),
                    reader.GetString(10), reader.GetFieldValue<DateTimeOffset>(11)));
        }
        var more = items.Count > pageSize;
        if (more) items.RemoveAt(pageSize);
        var next = more
            ? new CourierPayoutReconciliationAuditCursor(items[^1].OccurredAt, items[^1].ReadAuditId)
            : null;
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO courier_payout_reconciliation_search_read_audit
                (search_audit_id, actor_id, actor_filter, courier_id_filter, status_filter,
                 reconciliation_code_filter, outcome_filter, only_discrepancies_filter,
                 occurred_from_filter, occurred_to_filter, cursor_occurred_at, cursor_read_audit_id,
                 page_size, result_count, outcome_code, occurred_at)
            VALUES (@id, @actor, @actor_filter, @courier_filter, @status_filter,
                    @code_filter, @outcome_filter, @only_filter, @from_at, @to_at,
                    @before_at, @before_id, @page_size, @count, @result, @at);
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("id", NpgsqlDbType.Text, "cprs_" + Guid.NewGuid().ToString("N"));
            audit.Parameters.AddWithValue("actor", NpgsqlDbType.Text, operationsContext.ActorId);
            audit.Parameters.Add("actor_filter", NpgsqlDbType.Text).Value = actorFilter is null ? DBNull.Value : actorFilter;
            audit.Parameters.Add("courier_filter", NpgsqlDbType.Text).Value = courierIdFilter is null ? DBNull.Value : courierIdFilter;
            audit.Parameters.Add("status_filter", NpgsqlDbType.Text).Value = statusFilter is null ? DBNull.Value : statusFilter;
            audit.Parameters.Add("code_filter", NpgsqlDbType.Text).Value = reconciliationCodeFilter is null ? DBNull.Value : reconciliationCodeFilter;
            audit.Parameters.Add("outcome_filter", NpgsqlDbType.Text).Value = outcomeFilter is null ? DBNull.Value : outcomeFilter;
            audit.Parameters.Add("only_filter", NpgsqlDbType.Boolean).Value =
                onlyDiscrepanciesFilter is null ? DBNull.Value : onlyDiscrepanciesFilter;
            audit.Parameters.AddWithValue("from_at", NpgsqlDbType.TimestampTz, fromAt.ToUniversalTime());
            audit.Parameters.AddWithValue("to_at", NpgsqlDbType.TimestampTz, toAt.ToUniversalTime());
            audit.Parameters.Add("before_at", NpgsqlDbType.TimestampTz).Value =
                before is null ? DBNull.Value : before.OccurredAt.ToUniversalTime();
            audit.Parameters.Add("before_id", NpgsqlDbType.Text).Value =
                before is null ? DBNull.Value : before.ReadAuditId;
            audit.Parameters.AddWithValue("page_size", NpgsqlDbType.Integer, pageSize);
            audit.Parameters.AddWithValue("count", NpgsqlDbType.Integer, items.Count);
            audit.Parameters.AddWithValue("result", NpgsqlDbType.Text, items.Count == 0 ? "EMPTY" : "FOUND");
            audit.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new CourierPayoutReconciliationAuditPage(items, next);
    }

    private static bool IsCourierId(string value) => value.Length == 36 &&
        value.StartsWith("crr_", StringComparison.Ordinal) &&
        Guid.TryParseExact(value.AsSpan(4), "N", out _);
}
