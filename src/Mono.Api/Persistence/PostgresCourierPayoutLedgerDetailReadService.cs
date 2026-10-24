using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierPayoutLedgerDetailEntry(string AccountOwnerType, string AccountCode, string Currency, long DebitAmountToman, long CreditAmountToman, DateTimeOffset CreatedAt);
public sealed record CourierPayoutLedgerDetailTransaction(string LedgerTransactionId, string Currency, DateTimeOffset OccurredAt, IReadOnlyList<CourierPayoutLedgerDetailEntry> Entries);
public sealed record CourierPayoutLedgerDetail(string OutcomeCode, string WithdrawalId, string? CourierId, long? AmountToman, string? LatestAction, IReadOnlyList<CourierPayoutLedgerDetailTransaction> Transactions);

public sealed class PostgresCourierPayoutLedgerDetailReadService(NpgsqlDataSource dataSource, IMonoOperationsContext operationsContext)
{
    public async Task<CourierPayoutLedgerDetail> ReadAsync(string withdrawalId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("Administrator role is required.");
        if (!IsWithdrawalId(withdrawalId)) throw new ArgumentException("A valid withdrawal ID is required.", nameof(withdrawalId));
        const string sql = """
            WITH latest_event AS (
                SELECT action FROM courier_withdrawal_events WHERE withdrawal_id = @withdrawal_id
                ORDER BY withdrawal_event_sequence DESC LIMIT 1
            )
            SELECT request.courier_id, request.amount_toman, latest.action,
                   payout_tx.ledger_transaction_id, payout_tx.currency, payout_tx.occurred_at,
                   account.owner_type, account.account_code, entry.debit_amount,
                   entry.credit_amount, entry.created_at
            FROM courier_withdrawal_requests request
            LEFT JOIN latest_event latest ON true
            LEFT JOIN ledger_transactions payout_tx
              ON payout_tx.reference_type = 'WITHDRAWAL_PAYOUT' AND payout_tx.reference_id = request.withdrawal_id
            LEFT JOIN ledger_entries entry ON entry.ledger_transaction_id = payout_tx.ledger_transaction_id
            LEFT JOIN ledger_accounts account ON account.ledger_account_id = entry.ledger_account_id
            WHERE request.withdrawal_id = @withdrawal_id
            ORDER BY payout_tx.occurred_at, payout_tx.ledger_transaction_id, entry.created_at, entry.ledger_entry_id;
            """;
        string? courierId = null, latestAction = null;
        long? amountToman = null;
        var transactions = new List<CourierPayoutLedgerDetailTransaction>();
        var info = new Dictionary<string, (string Currency, DateTimeOffset At)>(StringComparer.Ordinal);
        var entries = new Dictionary<string, List<CourierPayoutLedgerDetailEntry>>(StringComparer.Ordinal);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("withdrawal_id", NpgsqlDbType.Text, withdrawalId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                courierId ??= reader.GetString(0);
                amountToman ??= reader.GetInt64(1);
                latestAction ??= reader.IsDBNull(2) ? null : reader.GetString(2);
                if (reader.IsDBNull(3)) continue;
                var txId = reader.GetString(3);
                if (!info.ContainsKey(txId))
                {
                    info.Add(txId, (reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5)));
                    entries.Add(txId, []);
                }
                if (!reader.IsDBNull(6))
                    entries[txId].Add(new CourierPayoutLedgerDetailEntry(reader.GetString(6),
                        reader.GetString(7), reader.GetString(4), reader.GetInt64(8),
                        reader.GetInt64(9), reader.GetFieldValue<DateTimeOffset>(10)));
            }
        }
        foreach (var (txId, item) in info)
            transactions.Add(new CourierPayoutLedgerDetailTransaction(txId, item.Currency, item.At, entries[txId]));
        var outcome = courierId is null ? "WITHDRAWAL_NOT_FOUND" : transactions.Count == 0 ? "PAYOUT_NOT_POSTED" : "FOUND";
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO courier_payout_ledger_detail_read_audit
                (read_audit_id, actor_id, withdrawal_id, outcome_code, transaction_count, entry_count, occurred_at)
            VALUES (@id, @actor, @withdrawal, @outcome, @tx_count, @entry_count, @at);
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("id", NpgsqlDbType.Text, "cpl_" + Guid.NewGuid().ToString("N"));
            audit.Parameters.AddWithValue("actor", NpgsqlDbType.Text, operationsContext.ActorId);
            audit.Parameters.AddWithValue("withdrawal", NpgsqlDbType.Text, withdrawalId);
            audit.Parameters.AddWithValue("outcome", NpgsqlDbType.Text, outcome);
            audit.Parameters.AddWithValue("tx_count", NpgsqlDbType.Integer, transactions.Count);
            audit.Parameters.AddWithValue("entry_count", NpgsqlDbType.Integer, transactions.Sum(x => x.Entries.Count));
            audit.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, now.ToUniversalTime());
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new CourierPayoutLedgerDetail(outcome, withdrawalId, courierId, amountToman, latestAction, transactions);
    }

    private static bool IsWithdrawalId(string value) => value.Length == 36 &&
        value.StartsWith("wdr_", StringComparison.Ordinal) && Guid.TryParseExact(value.AsSpan(4), "N", out _);
}