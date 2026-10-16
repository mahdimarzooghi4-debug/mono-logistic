using System.Data;
using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierAvailableBalance(long AmountToman, DateTimeOffset ObservedAt);

public sealed record CourierEarningHistoryItem(
    string MissionId, long AmountToman, DateTimeOffset OccurredAt, long LedgerEntryId);

public sealed record CourierEarningHistoryPage(
    IReadOnlyList<CourierEarningHistoryItem> Items, long? NextBeforeEntryId);

public sealed record CourierLedgerHistoryItem(
    string ReferenceType, string ReferenceId, long DebitAmountToman, long CreditAmountToman,
    DateTimeOffset OccurredAt, long LedgerEntryId);

public sealed record CourierLedgerHistoryPage(
    IReadOnlyList<CourierLedgerHistoryItem> Items, long? NextBeforeEntryId,
    long TotalDebitAmountToman, long TotalCreditAmountToman,
    long OpeningPostedBalanceToman, long ClosingPostedBalanceToman);

public sealed class PostgresCourierEarningLedger(NpgsqlDataSource dataSource)
{
    public async Task<CourierAvailableBalance> GetAvailableBalanceAsync(
        string courierId, DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH posted AS (
                SELECT COALESCE(SUM(entry.credit_amount - entry.debit_amount), 0)::bigint AS amount
                FROM ledger_accounts account
                LEFT JOIN ledger_entries entry ON entry.ledger_account_id = account.ledger_account_id
                WHERE account.owner_type = 'COURIER' AND account.owner_id = @courier_id
                  AND account.account_code = 'COURIER_AVAILABLE' AND account.currency = 'TOMAN'
            ), latest AS (
                SELECT DISTINCT ON (event.withdrawal_id) event.withdrawal_id, event.action
                FROM courier_withdrawal_events event
                JOIN courier_withdrawal_requests request USING (withdrawal_id)
                WHERE request.courier_id = @courier_id
                ORDER BY event.withdrawal_id, event.withdrawal_event_sequence DESC
            ), reserved AS (
                SELECT COALESCE(SUM(request.amount_toman), 0)::bigint AS amount
                FROM courier_withdrawal_requests request
                JOIN latest ON latest.withdrawal_id = request.withdrawal_id
                WHERE request.courier_id = @courier_id AND latest.action IN ('REQUESTED', 'PROCESSING')
            )
            SELECT posted.amount - reserved.amount FROM posted CROSS JOIN reserved;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        var amount = (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Courier balance query returned no result."));
        return new CourierAvailableBalance(amount, observedAt);
    }

    public async Task<CourierEarningHistoryPage> GetEarningHistoryAsync(
        string courierId, int limit, long? beforeEntryId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ledger_tx.reference_id, entry.credit_amount, ledger_tx.occurred_at,
                   entry.ledger_entry_id
            FROM ledger_accounts account
            JOIN ledger_entries entry ON entry.ledger_account_id = account.ledger_account_id
            JOIN ledger_transactions ledger_tx
              ON ledger_tx.ledger_transaction_id = entry.ledger_transaction_id
            WHERE account.owner_type = 'COURIER' AND account.owner_id = @courier_id
              AND account.account_code = 'COURIER_AVAILABLE' AND account.currency = 'TOMAN'
              AND ledger_tx.reference_type = 'MISSION_EARNING'
              AND entry.credit_amount > 0
              AND (@before_entry_id IS NULL OR entry.ledger_entry_id < @before_entry_id)
            ORDER BY entry.ledger_entry_id DESC
            LIMIT @take;
            """;
        var items = new List<CourierEarningHistoryItem>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        command.Parameters.AddWithValue("before_entry_id", NpgsqlDbType.Bigint,
            beforeEntryId.HasValue ? beforeEntryId.Value : DBNull.Value);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, checked(limit + 1));
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new CourierEarningHistoryItem(
                    reader.GetString(0), reader.GetInt64(1), reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetInt64(3)));
            }
        }
        var hasMore = items.Count > limit;
        if (hasMore)
            items.RemoveAt(limit);
        return new CourierEarningHistoryPage(items, hasMore ? items[^1].LedgerEntryId : null);
    }

    public async Task<CourierLedgerHistoryPage> GetLedgerHistoryAsync(
        string courierId, int limit, long? beforeEntryId, CancellationToken cancellationToken,
        DateTimeOffset? fromAt = null, DateTimeOffset? toAt = null)
    {
        if (limit is < 1 or > 100 || beforeEntryId is <= 0 ||
            fromAt.HasValue != toAt.HasValue ||
            (fromAt.HasValue && fromAt.Value >= toAt!.Value))
            throw new ArgumentOutOfRangeException(nameof(limit));

        const string sql = """
            SELECT ledger_tx.reference_type, ledger_tx.reference_id,
                   entry.debit_amount, entry.credit_amount, ledger_tx.occurred_at,
                   entry.ledger_entry_id
            FROM ledger_accounts account
            JOIN ledger_entries entry ON entry.ledger_account_id = account.ledger_account_id
            JOIN ledger_transactions ledger_tx
              ON ledger_tx.ledger_transaction_id = entry.ledger_transaction_id
            WHERE account.owner_type = 'COURIER' AND account.owner_id = @courier_id
              AND account.account_code = 'COURIER_AVAILABLE' AND account.currency = 'TOMAN'
              AND (@from_at IS NULL OR ledger_tx.occurred_at >= @from_at)
              AND (@to_at IS NULL OR ledger_tx.occurred_at < @to_at)
              AND (@before_entry_id IS NULL OR entry.ledger_entry_id < @before_entry_id)
            ORDER BY entry.ledger_entry_id DESC
            LIMIT @take;
            """;
        const string summarySql = """
            SELECT COALESCE(SUM(entry.credit_amount - entry.debit_amount) FILTER (
                       WHERE @from_at IS NOT NULL AND ledger_tx.occurred_at < @from_at), 0)::bigint,
                   COALESCE(SUM(entry.debit_amount) FILTER (
                       WHERE (@from_at IS NULL OR ledger_tx.occurred_at >= @from_at)
                         AND (@to_at IS NULL OR ledger_tx.occurred_at < @to_at)), 0)::bigint,
                   COALESCE(SUM(entry.credit_amount) FILTER (
                       WHERE (@from_at IS NULL OR ledger_tx.occurred_at >= @from_at)
                         AND (@to_at IS NULL OR ledger_tx.occurred_at < @to_at)), 0)::bigint,
                   COALESCE(SUM(entry.credit_amount - entry.debit_amount) FILTER (
                       WHERE @to_at IS NULL OR ledger_tx.occurred_at < @to_at), 0)::bigint
            FROM ledger_accounts account
            JOIN ledger_entries entry ON entry.ledger_account_id = account.ledger_account_id
            JOIN ledger_transactions ledger_tx
              ON ledger_tx.ledger_transaction_id = entry.ledger_transaction_id
            WHERE account.owner_type = 'COURIER' AND account.owner_id = @courier_id
              AND account.account_code = 'COURIER_AVAILABLE' AND account.currency = 'TOMAN';
            """;
        var items = new List<CourierLedgerHistoryItem>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken);
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("from_at", NpgsqlDbType.TimestampTz,
                fromAt.HasValue ? fromAt.Value.ToUniversalTime() : DBNull.Value);
            command.Parameters.AddWithValue("to_at", NpgsqlDbType.TimestampTz,
                toAt.HasValue ? toAt.Value.ToUniversalTime() : DBNull.Value);
            command.Parameters.AddWithValue("before_entry_id", NpgsqlDbType.Bigint,
                beforeEntryId.HasValue ? beforeEntryId.Value : DBNull.Value);
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, checked(limit + 1));
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new CourierLedgerHistoryItem(
                    reader.GetString(0), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3),
                    reader.GetFieldValue<DateTimeOffset>(4), reader.GetInt64(5)));
        }

        long openingPostedBalance;
        long totalDebit;
        long totalCredit;
        long closingPostedBalance;
        await using (var command = new NpgsqlCommand(summarySql, connection, transaction))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("from_at", NpgsqlDbType.TimestampTz,
                fromAt.HasValue ? fromAt.Value.ToUniversalTime() : DBNull.Value);
            command.Parameters.AddWithValue("to_at", NpgsqlDbType.TimestampTz,
                toAt.HasValue ? toAt.Value.ToUniversalTime() : DBNull.Value);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Courier ledger summary query returned no result.");
            openingPostedBalance = reader.GetInt64(0);
            totalDebit = reader.GetInt64(1);
            totalCredit = reader.GetInt64(2);
            closingPostedBalance = reader.GetInt64(3);
        }

        var hasMore = items.Count > limit;
        if (hasMore)
            items.RemoveAt(limit);
        await transaction.CommitAsync(cancellationToken);
        return new CourierLedgerHistoryPage(
            items, hasMore ? items[^1].LedgerEntryId : null, totalDebit, totalCredit,
            openingPostedBalance, closingPostedBalance);
    }

    public static async Task<bool> PostMissionEarningAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string customerId,
        string missionId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string schemaSql = """
            SELECT to_regclass('delivery_quote_versions') IS NOT NULL
               AND to_regclass('mission_courier_offer_events') IS NOT NULL;
            """;
        await using (var command = new NpgsqlCommand(schemaSql, connection, transaction))
        {
            if (!Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken)))
                return false;
        }

        const string missionSql = """
            SELECT (quote.snapshot->'fare'->>'fare_toman')::bigint,
                   (SELECT offer_event.courier_id
                    FROM mission_courier_offer_events offer_event
                    WHERE offer_event.mission_id = mission.mission_id
                      AND offer_event.customer_id = mission.customer_id
                      AND offer_event.action = 'COMPLETED'
                    ORDER BY offer_event.event_sequence DESC
                    LIMIT 1)
            FROM missions mission
            JOIN delivery_quote_versions quote
              ON quote.customer_id = mission.customer_id
             AND quote.delivery_request_id = mission.delivery_request_id
             AND quote.quote_id = mission.quote_id
             AND quote.version = mission.quote_version
            WHERE mission.mission_id = @mission_id
              AND mission.customer_id = @customer_id
              AND mission.status = 'DELIVERED';
            """;
        long fareToman;
        string? courierId;
        await using (var command = new NpgsqlCommand(missionSql, connection, transaction))
        {
            command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return false;
            if (reader.IsDBNull(0))
                throw new InvalidOperationException("A delivered Mission must retain its quoted fare.");
            fareToman = reader.GetInt64(0);
            courierId = reader.IsDBNull(1) ? null : reader.GetString(1);
        }
        if (courierId is null)
            throw new InvalidOperationException("A delivered Mission must have a completed courier assignment.");
        if (fareToman <= 0 || fareToman % 100 != 0)
            throw new InvalidOperationException("Mission fare is not divisible into the approved commission and VAT amounts.");

        var commissionToman = fareToman / 10;
        var vatToman = commissionToman / 10;
        var courierNetToman = fareToman - commissionToman - vatToman;
        if (courierNetToman <= 0 || checked(courierNetToman + commissionToman + vatToman) != fareToman)
            throw new InvalidOperationException("Mission earning allocation is invalid.");

        var customerReceivable = await GetOrCreateAccountAsync(connection, transaction,
            "CUSTOMER", customerId, "CUSTOMER_RECEIVABLE", occurredAt, cancellationToken);
        var courierAvailable = await GetOrCreateAccountAsync(connection, transaction,
            "COURIER", courierId, "COURIER_AVAILABLE", occurredAt, cancellationToken);
        var commissionRevenue = await GetOrCreateAccountAsync(connection, transaction,
            "MONO", "mono", "MONO_COMMISSION_REVENUE", occurredAt, cancellationToken);
        var vatPayable = await GetOrCreateAccountAsync(connection, transaction,
            "MONO", "mono", "VAT_PAYABLE", occurredAt, cancellationToken);

        var transactionId = "ltx_" + Guid.NewGuid().ToString("N");
        const string insertTransactionSql = """
            INSERT INTO ledger_transactions (
                ledger_transaction_id, reference_type, reference_id, idempotency_key,
                currency, occurred_at, created_at)
            VALUES (@transaction_id, 'MISSION_EARNING', @mission_id, @idempotency_key,
                    'TOMAN', @occurred_at, @created_at)
            ON CONFLICT (reference_type, reference_id) DO NOTHING;
            """;
        await using (var command = new NpgsqlCommand(insertTransactionSql, connection, transaction))
        {
            command.Parameters.AddWithValue("transaction_id", NpgsqlDbType.Text, transactionId);
            command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, "mission-earning:" + missionId);
            command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
            command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, occurredAt);
            if (await command.ExecuteNonQueryAsync(cancellationToken) == 0)
                return false;
        }

        await InsertEntryAsync(connection, transaction, transactionId, customerReceivable,
            fareToman, 0, occurredAt, cancellationToken);
        await InsertEntryAsync(connection, transaction, transactionId, courierAvailable,
            0, courierNetToman, occurredAt, cancellationToken);
        await InsertEntryAsync(connection, transaction, transactionId, commissionRevenue,
            0, commissionToman, occurredAt, cancellationToken);
        await InsertEntryAsync(connection, transaction, transactionId, vatPayable,
            0, vatToman, occurredAt, cancellationToken);
        return true;
    }

    private static async Task<string> GetOrCreateAccountAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string ownerType,
        string ownerId,
        string accountCode,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        const string insertSql = """
            INSERT INTO ledger_accounts (
                ledger_account_id, owner_type, owner_id, account_code, currency, created_at)
            VALUES (@account_id, @owner_type, @owner_id, @account_code, 'TOMAN', @created_at)
            ON CONFLICT (owner_type, owner_id, account_code, currency) DO NOTHING
            RETURNING ledger_account_id;
            """;
        await using (var command = new NpgsqlCommand(insertSql, connection, transaction))
        {
            command.Parameters.AddWithValue("account_id", NpgsqlDbType.Text, "lac_" + Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("owner_type", NpgsqlDbType.Text, ownerType);
            command.Parameters.AddWithValue("owner_id", NpgsqlDbType.Text, ownerId);
            command.Parameters.AddWithValue("account_code", NpgsqlDbType.Text, accountCode);
            command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, createdAt);
            var accountId = (string?)await command.ExecuteScalarAsync(cancellationToken);
            if (accountId is not null)
                return accountId;
        }

        const string selectSql = """
            SELECT ledger_account_id FROM ledger_accounts
            WHERE owner_type = @owner_type AND owner_id = @owner_id
              AND account_code = @account_code AND currency = 'TOMAN';
            """;
        await using var select = new NpgsqlCommand(selectSql, connection, transaction);
        select.Parameters.AddWithValue("owner_type", NpgsqlDbType.Text, ownerType);
        select.Parameters.AddWithValue("owner_id", NpgsqlDbType.Text, ownerId);
        select.Parameters.AddWithValue("account_code", NpgsqlDbType.Text, accountCode);
        return (string?)await select.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Ledger account could not be resolved.");
    }

    private static async Task InsertEntryAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string transactionId,
        string accountId,
        long debit,
        long credit,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO ledger_entries (
                ledger_transaction_id, ledger_account_id, debit_amount, credit_amount, created_at)
            VALUES (@transaction_id, @account_id, @debit, @credit, @created_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("transaction_id", NpgsqlDbType.Text, transactionId);
        command.Parameters.AddWithValue("account_id", NpgsqlDbType.Text, accountId);
        command.Parameters.AddWithValue("debit", NpgsqlDbType.Bigint, debit);
        command.Parameters.AddWithValue("credit", NpgsqlDbType.Bigint, credit);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
