using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record CourierPayoutDestination(string DestinationId, string MaskedIban);
public sealed record CourierWithdrawalItem(
    string WithdrawalId,
    string CourierId,
    long AmountToman,
    string MaskedIban,
    string Status,
    DateTimeOffset CreatedAt,
    string? ReasonCode,
    string? BankReference,
    long EventSequence);
public sealed record CourierWithdrawalPage(
    IReadOnlyList<CourierWithdrawalItem> Items,
    long? NextBeforeSequence);
public sealed record CourierWithdrawalWriteResult(
    string OutcomeCode,
    CourierWithdrawalItem? Withdrawal,
    bool AlreadyRequested);

public sealed class PostgresCourierWithdrawalService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    CourierPayoutIbanProtector ibanProtector)
{
    public async Task<CourierWithdrawalWriteResult> VerifyDestinationAsync(
        string courierId, string ibanInput, string evidenceReference, string idempotencyKey,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!CourierPayoutIbanProtector.TryNormalize(ibanInput, out var iban))
            return new CourierWithdrawalWriteResult("INVALID_IBAN", null, false);
        evidenceReference = evidenceReference.Trim();
        idempotencyKey = idempotencyKey.Trim();
        if (!IsSafeReference(evidenceReference, 256) || !IsSafeReference(idempotencyKey, 128))
            return new CourierWithdrawalWriteResult("INVALID_INPUT", null, false);
        if (!ibanProtector.IsConfigured)
            return new CourierWithdrawalWriteResult("ENCRYPTION_UNAVAILABLE", null, false);

        var actorId = operationsContext.ActorId;
        var fingerprint = CourierPayoutIbanProtector.Fingerprint(courierId, iban);
        var requestHash = Hash("VERIFY\n" + courierId + "\n" + fingerprint + "\n" + evidenceReference);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await LockCourierAsync(connection, transaction, courierId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CourierWithdrawalWriteResult("COURIER_NOT_FOUND", null, false);
        }

        const string priorSql = """
            SELECT destination_id, request_hash
            FROM courier_payout_destination_events
            WHERE actor_id = @actor_id AND idempotency_key = @idempotency_key AND action = 'VERIFIED';
            """;
        string? priorDestination = null;
        string? priorHash = null;
        await using (var command = new NpgsqlCommand(priorSql, connection, transaction))
        {
            command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                priorDestination = reader.GetString(0);
                priorHash = reader.GetString(1);
            }
        }
        if (priorDestination is not null)
        {
            if (!string.Equals(priorHash, requestHash, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CourierWithdrawalWriteResult("IDEMPOTENCY_CONFLICT", null, false);
            }
            await transaction.CommitAsync(cancellationToken);
            return new CourierWithdrawalWriteResult("VERIFIED", null, true);
        }

        var current = await FindCurrentDestinationAsync(connection, transaction, courierId, cancellationToken);
        if (current is not null && current.Value.Fingerprint != fingerprint)
        {
            await InsertDestinationEventAsync(connection, transaction, current.Value.DestinationId, courierId,
                "REVOKED", actorId, "rotate:" + current.Value.DestinationId,
                Hash("REVOKED\n" + current.Value.DestinationId), evidenceReference, now, cancellationToken);
        }

        const string destinationSql = """
            INSERT INTO courier_payout_destinations
                (destination_id, courier_id, encrypted_iban, iban_fingerprint, masked_iban, created_at)
            VALUES (@destination_id, @courier_id, @encrypted_iban, @fingerprint, @masked_iban, @created_at)
            ON CONFLICT (courier_id, iban_fingerprint) DO NOTHING
            RETURNING destination_id;
            """;
        var destinationId = "cpd_" + Guid.NewGuid().ToString("N");
        await using (var command = new NpgsqlCommand(destinationSql, connection, transaction))
        {
            command.Parameters.AddWithValue("destination_id", NpgsqlDbType.Text, destinationId);
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("encrypted_iban", NpgsqlDbType.Text, ibanProtector.Protect(courierId, iban));
            command.Parameters.AddWithValue("fingerprint", NpgsqlDbType.Text, fingerprint);
            command.Parameters.AddWithValue("masked_iban", NpgsqlDbType.Text, CourierPayoutIbanProtector.Mask(iban));
            command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            destinationId = (string?)await command.ExecuteScalarAsync(cancellationToken) ?? string.Empty;
        }
        if (destinationId.Length == 0)
        {
            const string findSql = """
                SELECT destination_id FROM courier_payout_destinations
                WHERE courier_id = @courier_id AND iban_fingerprint = @fingerprint;
                """;
            await using var command = new NpgsqlCommand(findSql, connection, transaction);
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("fingerprint", NpgsqlDbType.Text, fingerprint);
            destinationId = (string?)await command.ExecuteScalarAsync(cancellationToken) ?? string.Empty;
        }
        if (destinationId.Length == 0)
            throw new InvalidOperationException("Verified payout destination could not be resolved.");

        await InsertDestinationEventAsync(connection, transaction, destinationId, courierId,
            "VERIFIED", actorId, idempotencyKey, requestHash, evidenceReference, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierWithdrawalWriteResult("VERIFIED", null, false);
    }

    public async Task<CourierWithdrawalWriteResult> CreateWithdrawalAsync(
        string courierId, long amountToman, string idempotencyKey, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        idempotencyKey = idempotencyKey.Trim();
        if (amountToman <= 0 || !IsSafeReference(idempotencyKey, 128))
            return new CourierWithdrawalWriteResult("INVALID_INPUT", null, false);
        var requestHash = Hash("WITHDRAW\n" + courierId + "\n" + amountToman.ToString(CultureInfo.InvariantCulture));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (!await LockCourierAsync(connection, transaction, courierId, cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CourierWithdrawalWriteResult("COURIER_NOT_FOUND", null, false);
        }

        const string priorSql = """
            SELECT request.withdrawal_id, request.request_hash
            FROM courier_withdrawal_requests request
            WHERE request.courier_id = @courier_id AND request.idempotency_key = @idempotency_key;
            """;
        string? priorId = null;
        string? priorHash = null;
        await using (var command = new NpgsqlCommand(priorSql, connection, transaction))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                priorId = reader.GetString(0);
                priorHash = reader.GetString(1);
            }
        }
        if (priorId is not null)
        {
            if (!string.Equals(priorHash, requestHash, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CourierWithdrawalWriteResult("IDEMPOTENCY_CONFLICT", null, false);
            }
            var prior = await FindWithdrawalAsync(connection, transaction, priorId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CourierWithdrawalWriteResult(prior?.Status ?? "REQUESTED", prior, true);
        }

        var destination = await FindCurrentDestinationAsync(connection, transaction, courierId, cancellationToken);
        if (destination is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CourierWithdrawalWriteResult("PAYOUT_DESTINATION_UNVERIFIED", null, false);
        }

        var (dayStart, nextDayStart) = TehranDayBounds(now);
        const string countSql = """
            SELECT COUNT(*) FROM courier_withdrawal_requests
            WHERE courier_id = @courier_id AND created_at >= @day_start AND created_at < @next_day_start;
            """;
        await using (var command = new NpgsqlCommand(countSql, connection, transaction))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("day_start", NpgsqlDbType.TimestampTz, dayStart);
            command.Parameters.AddWithValue("next_day_start", NpgsqlDbType.TimestampTz, nextDayStart);
            if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) >= 3)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CourierWithdrawalWriteResult("DAILY_LIMIT_REACHED", null, false);
            }
        }

        var available = await GetAvailableBalanceAsync(connection, transaction, courierId, cancellationToken);
        if (amountToman > available)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CourierWithdrawalWriteResult("INSUFFICIENT_AVAILABLE_BALANCE", null, false);
        }

        var withdrawalId = "wdr_" + Guid.NewGuid().ToString("N");
        const string insertSql = """
            INSERT INTO courier_withdrawal_requests
                (withdrawal_id, courier_id, destination_id, amount_toman, idempotency_key, request_hash, created_at)
            VALUES (@withdrawal_id, @courier_id, @destination_id, @amount, @idempotency_key, @request_hash, @created_at);
            """;
        await using (var command = new NpgsqlCommand(insertSql, connection, transaction))
        {
            command.Parameters.AddWithValue("withdrawal_id", NpgsqlDbType.Text, withdrawalId);
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("destination_id", NpgsqlDbType.Text, destination.Value.DestinationId);
            command.Parameters.AddWithValue("amount", NpgsqlDbType.Bigint, amountToman);
            command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            command.Parameters.AddWithValue("request_hash", NpgsqlDbType.Text, requestHash);
            command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertWithdrawalEventAsync(connection, transaction, withdrawalId, "REQUESTED", "COURIER",
            courierId, idempotencyKey, requestHash, null, null, now, cancellationToken);
        var item = await FindWithdrawalAsync(connection, transaction, withdrawalId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierWithdrawalWriteResult("REQUESTED", item, false);
    }

    public async Task<CourierWithdrawalWriteResult> TransitionAsync(
        string withdrawalId, string action, string? reasonCode, string? bankReference,
        string idempotencyKey, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var actorId = operationsContext.ActorId;
        action = action.Trim().ToUpperInvariant();
        reasonCode = string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim().ToUpperInvariant();
        bankReference = string.IsNullOrWhiteSpace(bankReference) ? null : bankReference.Trim();
        idempotencyKey = idempotencyKey.Trim();
        if (!IsSafeReference(idempotencyKey, 128) ||
            (action == "PAID" && (!IsSafeReference(bankReference, 128) || reasonCode is not null)) ||
            (action == "REJECTED" && (!IsReasonCode(reasonCode) || bankReference is not null)) ||
            (action == "PROCESSING" && (reasonCode is not null || bankReference is not null)) ||
            action is not ("PROCESSING" or "PAID" or "REJECTED"))
            return new CourierWithdrawalWriteResult("INVALID_INPUT", null, false);

        var requestHash = Hash(action + "\n" + (reasonCode ?? "") + "\n" + (bankReference ?? ""));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string courierSql = "SELECT courier_id FROM courier_withdrawal_requests WHERE withdrawal_id = @withdrawal_id;";
        string? courierId;
        await using (var command = new NpgsqlCommand(courierSql, connection, transaction))
        {
            command.Parameters.AddWithValue("withdrawal_id", NpgsqlDbType.Text, withdrawalId);
            courierId = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }
        if (courierId is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CourierWithdrawalWriteResult("NOT_FOUND", null, false);
        }
        if (!await LockCourierAsync(connection, transaction, courierId, cancellationToken))
            throw new InvalidOperationException("Withdrawal courier disappeared.");
        const string lockSql = """
            SELECT amount_toman FROM courier_withdrawal_requests
            WHERE withdrawal_id = @withdrawal_id FOR UPDATE;
            """;
        long amount;
        await using (var command = new NpgsqlCommand(lockSql, connection, transaction))
        {
            command.Parameters.AddWithValue("withdrawal_id", NpgsqlDbType.Text, withdrawalId);
            amount = (long)(await command.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Withdrawal disappeared during transition."));
        }

        const string priorSql = """
            SELECT request_hash FROM courier_withdrawal_events
            WHERE withdrawal_id = @withdrawal_id AND idempotency_key = @idempotency_key;
            """;
        string? priorHash;
        await using (var command = new NpgsqlCommand(priorSql, connection, transaction))
        {
            command.Parameters.AddWithValue("withdrawal_id", NpgsqlDbType.Text, withdrawalId);
            command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            priorHash = (string?)await command.ExecuteScalarAsync(cancellationToken);
        }
        if (priorHash is not null)
        {
            if (!string.Equals(priorHash, requestHash, StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(cancellationToken);
                return new CourierWithdrawalWriteResult("IDEMPOTENCY_CONFLICT", null, false);
            }
            var replay = await FindWithdrawalAsync(connection, transaction, withdrawalId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new CourierWithdrawalWriteResult(replay?.Status ?? "NOT_FOUND", replay, true);
        }

        var current = await GetWithdrawalStatusAsync(connection, transaction, withdrawalId, cancellationToken);
        var allowed = action switch
        {
            "PROCESSING" => current == "REQUESTED",
            "PAID" => current == "PROCESSING",
            "REJECTED" => current is "REQUESTED" or "PROCESSING",
            _ => false
        };
        if (!allowed)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new CourierWithdrawalWriteResult("INVALID_TRANSITION", null, false);
        }

        if (action == "PAID")
            await PostPayoutLedgerEntryAsync(connection, transaction, withdrawalId, courierId, amount, now, cancellationToken);

        await InsertWithdrawalEventAsync(connection, transaction, withdrawalId, action, "OPERATIONS",
            actorId, idempotencyKey, requestHash, reasonCode, bankReference, now, cancellationToken);
        var updated = await FindWithdrawalAsync(connection, transaction, withdrawalId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierWithdrawalWriteResult(action, updated, false);
    }

    public async Task<CourierWithdrawalPage> ListForCourierAsync(
        string courierId, int limit, long? beforeSequence, CancellationToken cancellationToken) =>
        await ListAsync(courierId, false, limit, beforeSequence, cancellationToken);

    public async Task<CourierWithdrawalPage> ListForOperationsAsync(
        int limit, long? beforeSequence, CancellationToken cancellationToken) =>
        await ListAsync(null, true, limit, beforeSequence, cancellationToken);

    private async Task<CourierWithdrawalPage> ListAsync(
        string? courierId, bool operations, int limit, long? beforeSequence, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH latest AS (
                SELECT DISTINCT ON (event.withdrawal_id)
                    event.withdrawal_id, event.action, event.reason_code, event.bank_reference,
                    event.withdrawal_event_sequence
                FROM courier_withdrawal_events event
                ORDER BY event.withdrawal_id, event.withdrawal_event_sequence DESC
            )
            SELECT request.withdrawal_id, request.courier_id, request.amount_toman,
                   destination.masked_iban, latest.action, request.created_at,
                   latest.reason_code, latest.bank_reference, latest.withdrawal_event_sequence
            FROM courier_withdrawal_requests request
            JOIN latest ON latest.withdrawal_id = request.withdrawal_id
            JOIN courier_payout_destinations destination ON destination.destination_id = request.destination_id
            WHERE (@courier_id IS NULL OR request.courier_id = @courier_id)
              AND (@operations = TRUE OR latest.withdrawal_event_sequence < COALESCE(@before_sequence, 9223372036854775807))
              AND (@operations = FALSE OR @before_sequence IS NULL OR latest.withdrawal_event_sequence < @before_sequence)
            ORDER BY latest.withdrawal_event_sequence DESC
            LIMIT @take;
            """;
        // Operations queue includes only requests still awaiting an operator decision.
        var query = operations ? sql.Replace(
            "WHERE (@courier_id IS NULL OR request.courier_id = @courier_id)",
            "WHERE latest.action IN ('REQUESTED', 'PROCESSING')", StringComparison.Ordinal) : sql;
        var items = new List<CourierWithdrawalItem>();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId is null ? DBNull.Value : courierId);
        command.Parameters.AddWithValue("operations", NpgsqlDbType.Boolean, operations);
        command.Parameters.AddWithValue("before_sequence", NpgsqlDbType.Bigint,
            beforeSequence.HasValue ? beforeSequence.Value : DBNull.Value);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, checked(limit + 1));
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                items.Add(new CourierWithdrawalItem(reader.GetString(0), reader.GetString(1),
                    reader.GetInt64(2), reader.GetString(3), reader.GetString(4),
                    reader.GetFieldValue<DateTimeOffset>(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7), reader.GetInt64(8)));
            }
        }
        var hasMore = items.Count > limit;
        if (hasMore)
            items.RemoveAt(limit);
        return new CourierWithdrawalPage(items, hasMore ? items[^1].EventSequence : null);
    }

    private static async Task<long> GetAvailableBalanceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string courierId,
        CancellationToken cancellationToken)
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
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Courier available balance could not be resolved."));
    }

    private static async Task<CourierWithdrawalItem?> FindWithdrawalAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string withdrawalId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT request.withdrawal_id, request.courier_id, request.amount_toman,
                   destination.masked_iban, latest.action, request.created_at,
                   latest.reason_code, latest.bank_reference, latest.withdrawal_event_sequence
            FROM courier_withdrawal_requests request
            JOIN courier_payout_destinations destination ON destination.destination_id = request.destination_id
            JOIN LATERAL (
                SELECT action, reason_code, bank_reference, withdrawal_event_sequence
                FROM courier_withdrawal_events
                WHERE withdrawal_id = request.withdrawal_id
                ORDER BY withdrawal_event_sequence DESC LIMIT 1
            ) latest ON TRUE
            WHERE request.withdrawal_id = @withdrawal_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("withdrawal_id", NpgsqlDbType.Text, withdrawalId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return new CourierWithdrawalItem(reader.GetString(0), reader.GetString(1), reader.GetInt64(2),
            reader.GetString(3), reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.GetInt64(8));
    }

    private static async Task<string> GetWithdrawalStatusAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string withdrawalId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT action FROM courier_withdrawal_events
            WHERE withdrawal_id = @withdrawal_id ORDER BY withdrawal_event_sequence DESC LIMIT 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("withdrawal_id", NpgsqlDbType.Text, withdrawalId);
        return (string?)await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Withdrawal status event is missing.");
    }

    private async Task PostPayoutLedgerEntryAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string withdrawalId,
        string courierId, long amount, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var courierAccountId = await FindAccountAsync(connection, transaction, "COURIER", courierId,
            "COURIER_AVAILABLE", cancellationToken);
        if (courierAccountId is null)
            throw new InvalidOperationException("Courier available ledger account is missing.");
        var clearingAccountId = await GetOrCreateMonoClearingAccountAsync(
            connection, transaction, now, cancellationToken);
        var transactionId = "ltx_" + Guid.NewGuid().ToString("N");
        const string transactionSql = """
            INSERT INTO ledger_transactions
                (ledger_transaction_id, reference_type, reference_id, idempotency_key, currency, occurred_at, created_at)
            VALUES (@transaction_id, 'WITHDRAWAL_PAYOUT', @withdrawal_id,
                    @idempotency_key, 'TOMAN', @occurred_at, @created_at);
            """;
        await using (var command = new NpgsqlCommand(transactionSql, connection, transaction))
        {
            command.Parameters.AddWithValue("transaction_id", NpgsqlDbType.Text, transactionId);
            command.Parameters.AddWithValue("withdrawal_id", NpgsqlDbType.Text, withdrawalId);
            command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, "withdrawal-payout:" + withdrawalId);
            command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
            command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await InsertLedgerEntryAsync(connection, transaction, transactionId, courierAccountId, amount, 0, now, cancellationToken);
        await InsertLedgerEntryAsync(connection, transaction, transactionId, clearingAccountId, 0, amount, now, cancellationToken);
    }

    private static async Task<string> GetOrCreateMonoClearingAccountAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await FindAccountAsync(connection, transaction, "MONO", "mono",
            "MONO_PAYOUT_CLEARING", cancellationToken);
        if (existing is not null)
            return existing;
        const string sql = """
            INSERT INTO ledger_accounts
                (ledger_account_id, owner_type, owner_id, account_code, currency, created_at)
            VALUES (@account_id, 'MONO', 'mono', 'MONO_PAYOUT_CLEARING', 'TOMAN', @created_at)
            ON CONFLICT (owner_type, owner_id, account_code, currency) DO NOTHING
            RETURNING ledger_account_id;
            """;
        await using (var command = new NpgsqlCommand(sql, connection, transaction))
        {
            command.Parameters.AddWithValue("account_id", NpgsqlDbType.Text, "lac_" + Guid.NewGuid().ToString("N"));
            command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            var id = (string?)await command.ExecuteScalarAsync(cancellationToken);
            if (id is not null)
                return id;
        }
        return await FindAccountAsync(connection, transaction, "MONO", "mono",
            "MONO_PAYOUT_CLEARING", cancellationToken)
            ?? throw new InvalidOperationException("Mono payout clearing account could not be resolved.");
    }

    private static async Task<string?> FindAccountAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string ownerType,
        string ownerId, string accountCode, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT ledger_account_id FROM ledger_accounts
            WHERE owner_type = @owner_type AND owner_id = @owner_id
              AND account_code = @account_code AND currency = 'TOMAN';
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("owner_type", NpgsqlDbType.Text, ownerType);
        command.Parameters.AddWithValue("owner_id", NpgsqlDbType.Text, ownerId);
        command.Parameters.AddWithValue("account_code", NpgsqlDbType.Text, accountCode);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task InsertLedgerEntryAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string transactionId,
        string accountId, long debit, long credit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO ledger_entries
                (ledger_transaction_id, ledger_account_id, debit_amount, credit_amount, created_at)
            VALUES (@transaction_id, @account_id, @debit, @credit, @created_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("transaction_id", NpgsqlDbType.Text, transactionId);
        command.Parameters.AddWithValue("account_id", NpgsqlDbType.Text, accountId);
        command.Parameters.AddWithValue("debit", NpgsqlDbType.Bigint, debit);
        command.Parameters.AddWithValue("credit", NpgsqlDbType.Bigint, credit);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> LockCourierAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string courierId,
        CancellationToken cancellationToken)
    {
        const string sql = "SELECT courier_id FROM couriers WHERE courier_id = @courier_id FOR UPDATE;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    private static async Task<(string DestinationId, string Fingerprint)?> FindCurrentDestinationAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string courierId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT destination.destination_id, destination.iban_fingerprint
            FROM courier_payout_destinations destination
            JOIN LATERAL (
                SELECT action FROM courier_payout_destination_events
                WHERE destination_id = destination.destination_id
                ORDER BY destination_event_sequence DESC LIMIT 1
            ) latest ON latest.action = 'VERIFIED'
            WHERE destination.courier_id = @courier_id
            ORDER BY destination.created_at DESC LIMIT 1;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? (reader.GetString(0), reader.GetString(1))
            : null;
    }

    private static async Task InsertDestinationEventAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string destinationId,
        string courierId, string action, string actorId, string idempotencyKey, string requestHash,
        string evidenceReference, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO courier_payout_destination_events
                (destination_id, courier_id, action, actor_id, idempotency_key, request_hash,
                 evidence_reference, occurred_at)
            VALUES (@destination_id, @courier_id, @action, @actor_id, @idempotency_key, @request_hash,
                    @evidence_reference, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("destination_id", NpgsqlDbType.Text, destinationId);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
        command.Parameters.AddWithValue("request_hash", NpgsqlDbType.Text, requestHash);
        command.Parameters.AddWithValue("evidence_reference", NpgsqlDbType.Text, evidenceReference);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertWithdrawalEventAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string withdrawalId,
        string action, string actorType, string actorId, string idempotencyKey, string requestHash,
        string? reasonCode, string? bankReference, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO courier_withdrawal_events
                (withdrawal_id, action, actor_type, actor_id, idempotency_key, request_hash,
                 reason_code, bank_reference, occurred_at)
            VALUES (@withdrawal_id, @action, @actor_type, @actor_id, @idempotency_key, @request_hash,
                    @reason_code, @bank_reference, @occurred_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("withdrawal_id", NpgsqlDbType.Text, withdrawalId);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("actor_type", NpgsqlDbType.Text, actorType);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
        command.Parameters.AddWithValue("request_hash", NpgsqlDbType.Text, requestHash);
        command.Parameters.AddWithValue("reason_code", NpgsqlDbType.Text, reasonCode ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("bank_reference", NpgsqlDbType.Text, bankReference ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(DateTimeOffset Start, DateTimeOffset End)> TehranDayBounds(DateTimeOffset now)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var startLocal = DateTime.SpecifyKind(local.Date, DateTimeKind.Unspecified);
        var endLocal = startLocal.AddDays(1);
        return (new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal, zone)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(endLocal, zone)));
    }

    private static bool IsSafeReference(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength &&
        value == value.Trim() && value.All(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or ':' or '/');

    private static bool IsReasonCode(string? value) =>
        value is { Length: >= 2 and <= 64 } && value[0] is >= 'A' and <= 'Z' &&
        value.All(character => character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_');

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
