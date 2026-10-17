using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierPayoutLedgerDetailAuditCursor(DateTimeOffset OccurredAt, string ReadAuditId);
public sealed record CourierPayoutLedgerDetailAuditItem(
    string ReadAuditId, string ActorId, string WithdrawalId, string OutcomeCode,
    int TransactionCount, int EntryCount, DateTimeOffset OccurredAt);
public sealed record CourierPayoutLedgerDetailAuditPage(
    IReadOnlyList<CourierPayoutLedgerDetailAuditItem> Items, CourierPayoutLedgerDetailAuditCursor? NextCursor);

public sealed class PostgresCourierPayoutLedgerDetailAuditSearchService(
    NpgsqlDataSource dataSource, IMonoOperationsContext operationsContext)
{
    private static readonly string[] Outcomes = ["FOUND", "PAYOUT_NOT_POSTED", "WITHDRAWAL_NOT_FOUND"];

    public async Task<CourierPayoutLedgerDetailAuditPage> ReadAsync(
        int pageSize, string? actorFilter, string? withdrawalIdFilter, string? outcomeFilter,
        DateTimeOffset fromAt, DateTimeOffset toAt, CourierPayoutLedgerDetailAuditCursor? before,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Administrator role is required.");
        if (pageSize is < 1 or > 100 || fromAt >= toAt)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        actorFilter = string.IsNullOrWhiteSpace(actorFilter) ? null : actorFilter.Trim();
        if (actorFilter is { Length: > 128 }) throw new ArgumentException("Actor filter is too long.", nameof(actorFilter));
        if (withdrawalIdFilter is not null && !IsWithdrawalId(withdrawalIdFilter))
            throw new ArgumentException("A valid withdrawal ID is required.", nameof(withdrawalIdFilter));
        if (outcomeFilter is not null && !Outcomes.Contains(outcomeFilter, StringComparer.Ordinal))
            throw new ArgumentException("Unknown audit outcome.", nameof(outcomeFilter));

        const string sql = """
            SELECT read_audit_id, actor_id, withdrawal_id, outcome_code,
                   transaction_count, entry_count, occurred_at
            FROM courier_payout_ledger_detail_read_audit
            WHERE occurred_at >= @from_at AND occurred_at < @to_at
              AND (@actor_filter IS NULL OR actor_id = @actor_filter)
              AND (@withdrawal_filter IS NULL OR withdrawal_id = @withdrawal_filter)
              AND (@outcome_filter IS NULL OR outcome_code = @outcome_filter)
              AND (@before_at IS NULL OR (occurred_at, read_audit_id) < (@before_at, @before_id))
            ORDER BY occurred_at DESC, read_audit_id DESC
            LIMIT @take;
            """;
        var items = new List<CourierPayoutLedgerDetailAuditItem>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("from_at", NpgsqlDbType.TimestampTz, fromAt.ToUniversalTime());
            command.Parameters.AddWithValue("to_at", NpgsqlDbType.TimestampTz, toAt.ToUniversalTime());
            command.Parameters.Add("actor_filter", NpgsqlDbType.Text).Value = actorFilter is null ? DBNull.Value : actorFilter;
            command.Parameters.Add("withdrawal_filter", NpgsqlDbType.Text).Value = withdrawalIdFilter is null ? DBNull.Value : withdrawalIdFilter;
            command.Parameters.Add("outcome_filter", NpgsqlDbType.Text).Value = outcomeFilter is null ? DBNull.Value : outcomeFilter;
            command.Parameters.Add("before_at", NpgsqlDbType.TimestampTz).Value = before is null ? DBNull.Value : before.OccurredAt.ToUniversalTime();
            command.Parameters.Add("before_id", NpgsqlDbType.Text).Value = before is null ? DBNull.Value : before.ReadAuditId;
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new CourierPayoutLedgerDetailAuditItem(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetInt32(4), reader.GetInt32(5), reader.GetFieldValue<DateTimeOffset>(6)));
        }
        var more = items.Count > pageSize;
        if (more) items.RemoveAt(pageSize);
        var next = more ? new CourierPayoutLedgerDetailAuditCursor(items[^1].OccurredAt, items[^1].ReadAuditId) : null;
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO courier_payout_ledger_detail_search_read_audit
                (search_audit_id, actor_id, actor_filter, withdrawal_id_filter, outcome_filter,
                 occurred_from_filter, occurred_to_filter, cursor_occurred_at, cursor_read_audit_id,
                 page_size, result_count, outcome_code, occurred_at)
            VALUES (@id, @actor, @actor_filter, @withdrawal_filter, @outcome_filter,
                    @from_at, @to_at, @cursor_at, @cursor_id, @page_size, @count, @result, @at);
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("id", NpgsqlDbType.Text, "cps_" + Guid.NewGuid().ToString("N"));
            audit.Parameters.AddWithValue("actor", NpgsqlDbType.Text, operationsContext.ActorId);
            audit.Parameters.Add("actor_filter", NpgsqlDbType.Text).Value = actorFilter is null ? DBNull.Value : actorFilter;
            audit.Parameters.Add("withdrawal_filter", NpgsqlDbType.Text).Value = withdrawalIdFilter is null ? DBNull.Value : withdrawalIdFilter;
            audit.Parameters.Add("outcome_filter", NpgsqlDbType.Text).Value = outcomeFilter is null ? DBNull.Value : outcomeFilter;
            audit.Parameters.AddWithValue("from_at", NpgsqlDbType.TimestampTz, fromAt.ToUniversalTime());
            audit.Parameters.AddWithValue("to_at", NpgsqlDbType.TimestampTz, toAt.ToUniversalTime());
            audit.Parameters.Add("cursor_at", NpgsqlDbType.TimestampTz).Value = before is null ? DBNull.Value : before.OccurredAt.ToUniversalTime();
            audit.Parameters.Add("cursor_id", NpgsqlDbType.Text).Value = before is null ? DBNull.Value : before.ReadAuditId;
            audit.Parameters.AddWithValue("page_size", NpgsqlDbType.Integer, pageSize);
            audit.Parameters.AddWithValue("count", NpgsqlDbType.Integer, items.Count);
            audit.Parameters.AddWithValue("result", NpgsqlDbType.Text, items.Count == 0 ? "EMPTY" : "FOUND");
            audit.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new CourierPayoutLedgerDetailAuditPage(items, next);
    }

    private static bool IsWithdrawalId(string value) => value.Length == 36 &&
        value.StartsWith("wdr_", StringComparison.Ordinal) && Guid.TryParseExact(value.AsSpan(4), "N", out _);
}