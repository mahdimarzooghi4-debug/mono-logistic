using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierPayoutReconciliationTrendBucket(
    DateOnly UtcDate, string ReconciliationCode, long WithdrawalCount, long AmountToman);
public sealed record CourierPayoutReconciliationTrend(
    string OutcomeCode, long TotalWithdrawalCount, long DiscrepancyCount, long TotalAmountToman,
    IReadOnlyList<CourierPayoutReconciliationTrendBucket> Buckets);

public sealed class PostgresCourierPayoutReconciliationTrendService(
    NpgsqlDataSource dataSource, IMonoOperationsContext operationsContext)
{
    private static readonly string[] Statuses = ["REQUESTED", "PROCESSING", "PAID", "REJECTED"];

    public async Task<CourierPayoutReconciliationTrend> ReadAsync(
        string? courierId, string? status, DateTimeOffset fromAt, DateTimeOffset toAt,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Administrator role is required.");
        if (fromAt >= toAt) throw new ArgumentOutOfRangeException(nameof(fromAt));
        if (courierId is not null && !IsCourierId(courierId))
            throw new ArgumentException("A valid courier ID is required.", nameof(courierId));
        if (status is not null && !Statuses.Contains(status, StringComparer.Ordinal))
            throw new ArgumentException("Unknown withdrawal status.", nameof(status));

        var sql = CourierPayoutReconciliationSql.CommonCtes + """
            SELECT (created_at AT TIME ZONE 'UTC')::date AS utc_date,
                   reconciliation_code, COUNT(*)::bigint AS withdrawal_count,
                   COALESCE(SUM(amount_toman), 0)::bigint AS amount_toman
            FROM reconciled
            GROUP BY utc_date, reconciliation_code
            ORDER BY utc_date, reconciliation_code;
            """;
        var buckets = new List<CourierPayoutReconciliationTrendBucket>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("from_at", NpgsqlDbType.TimestampTz, fromAt.ToUniversalTime());
            command.Parameters.AddWithValue("to_at", NpgsqlDbType.TimestampTz, toAt.ToUniversalTime());
            command.Parameters.Add("courier_id", NpgsqlDbType.Text).Value = courierId is null ? DBNull.Value : courierId;
            command.Parameters.Add("status", NpgsqlDbType.Text).Value = status is null ? DBNull.Value : status;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                buckets.Add(new CourierPayoutReconciliationTrendBucket(
                    reader.GetFieldValue<DateOnly>(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3)));
        }
        var totalCount = buckets.Sum(item => item.WithdrawalCount);
        var discrepancyCount = buckets.Where(item => item.ReconciliationCode != "MATCHED").Sum(item => item.WithdrawalCount);
        var totalAmount = buckets.Sum(item => item.AmountToman);
        var outcome = totalCount == 0 ? "EMPTY" : "FOUND";
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO courier_payout_reconciliation_trend_read_audit
                (read_audit_id, actor_id, courier_id_filter, status_filter, created_from_filter,
                 created_to_filter, total_withdrawal_count, discrepancy_count, total_amount_toman,
                 bucket_count, outcome_code, occurred_at)
            VALUES (@id, @actor, @courier, @status, @from_at, @to_at, @total_count,
                    @discrepancies, @amount, @buckets, @outcome, @at);
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("id", NpgsqlDbType.Text, "cpt_" + Guid.NewGuid().ToString("N"));
            audit.Parameters.AddWithValue("actor", NpgsqlDbType.Text, operationsContext.ActorId);
            audit.Parameters.Add("courier", NpgsqlDbType.Text).Value = courierId is null ? DBNull.Value : courierId;
            audit.Parameters.Add("status", NpgsqlDbType.Text).Value = status is null ? DBNull.Value : status;
            audit.Parameters.AddWithValue("from_at", NpgsqlDbType.TimestampTz, fromAt.ToUniversalTime());
            audit.Parameters.AddWithValue("to_at", NpgsqlDbType.TimestampTz, toAt.ToUniversalTime());
            audit.Parameters.AddWithValue("total_count", NpgsqlDbType.Bigint, totalCount);
            audit.Parameters.AddWithValue("discrepancies", NpgsqlDbType.Bigint, discrepancyCount);
            audit.Parameters.AddWithValue("amount", NpgsqlDbType.Bigint, totalAmount);
            audit.Parameters.AddWithValue("buckets", NpgsqlDbType.Integer, buckets.Count);
            audit.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, outcome);
            audit.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new CourierPayoutReconciliationTrend(outcome, totalCount, discrepancyCount, totalAmount, buckets);
    }

    private static bool IsCourierId(string value) => value.Length == 36 &&
        value.StartsWith("crr_", StringComparison.Ordinal) && Guid.TryParseExact(value.AsSpan(4), "N", out _);
}