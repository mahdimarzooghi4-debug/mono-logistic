using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Mono.Api.Tests;

public sealed class CourierWithdrawalTests
{
    [Fact]
    public async Task Verified_destination_withdrawal_reserves_then_pays_or_releases_balance_idempotently()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        const string courierId = "crr_11111111111111111111111111111111";
        const string customerId = "cus_11111111111111111111111111111111";
        const string missionId = "mis_11111111111111111111111111111111";
        const string iban = "IR641234567890123456789012";
        var schemaName = "courier_withdrawals_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(connectionString))
        {
            await admin.OpenAsync();
            await using var createSchema = new NpgsqlCommand($"CREATE SCHEMA {schemaName};", admin);
            await createSchema.ExecuteNonQueryAsync();
        }

        try
        {
            var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schemaName };
            await using var dataSource = NpgsqlDataSource.Create(scoped.ConnectionString);
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var setup = new NpgsqlCommand("""
                CREATE TABLE couriers (courier_id text PRIMARY KEY);
                CREATE TABLE missions (
                    mission_id text PRIMARY KEY, customer_id text NOT NULL,
                    delivery_request_id text NOT NULL, quote_id text NOT NULL,
                    quote_version integer NOT NULL, status text NOT NULL);
                CREATE TABLE delivery_quote_versions (
                    customer_id text NOT NULL, delivery_request_id text NOT NULL,
                    quote_id text NOT NULL, version integer NOT NULL, snapshot jsonb NOT NULL);
                CREATE TABLE mission_courier_offer_events (
                    event_sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    mission_id text NOT NULL, customer_id text NOT NULL,
                    courier_id text NOT NULL, action text NOT NULL);
                INSERT INTO couriers VALUES ('crr_11111111111111111111111111111111');
                INSERT INTO missions VALUES
                    ('mis_11111111111111111111111111111111', 'cus_11111111111111111111111111111111',
                     'drq_1', 'quo_1', 1, 'DELIVERED');
                INSERT INTO delivery_quote_versions VALUES
                    ('cus_11111111111111111111111111111111', 'drq_1', 'quo_1', 1,
                     '{"fare":{"fare_toman":50000}}'::jsonb);
                INSERT INTO mission_courier_offer_events (mission_id, customer_id, courier_id, action)
                VALUES ('mis_11111111111111111111111111111111', 'cus_11111111111111111111111111111111',
                        'crr_11111111111111111111111111111111', 'COMPLETED');
                """, connection))
                await setup.ExecuteNonQueryAsync();

            await ApplyMigrationAsync(dataSource);
            var now = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                Assert.True(await PostgresCourierEarningLedger.PostMissionEarningAsync(
                    connection, transaction, customerId, missionId, now, CancellationToken.None));
                await transaction.CommitAsync();
            }

            Assert.True(CourierPayoutIbanProtector.TryNormalize(iban, out var normalized));
            Assert.Equal(iban, normalized);
            Assert.False(CourierPayoutIbanProtector.TryNormalize("IR001234567890123456789012", out _));

            var operations = new TestOperationsContext();
            var protector = new CourierPayoutIbanProtector(Convert.ToBase64String(new byte[32]));
            var withdrawals = new PostgresCourierWithdrawalService(dataSource, operations, protector);
            var verification = await withdrawals.VerifyDestinationAsync(courierId, iban,
                "case:bank-proof-001", "verify-iban-001", now, CancellationToken.None);
            Assert.Equal("VERIFIED", verification.OutcomeCode);

            var destinationStatus = await withdrawals.GetDestinationStatusAsync(
                courierId, CancellationToken.None);
            Assert.Equal("VERIFIED", destinationStatus.OutcomeCode);
            Assert.Equal("IR********************9012", destinationStatus.MaskedIban);
            var unverifiedDestination = await withdrawals.GetDestinationStatusAsync(
                "crr_22222222222222222222222222222222", CancellationToken.None);
            Assert.Equal("NOT_VERIFIED", unverifiedDestination.OutcomeCode);
            Assert.Null(unverifiedDestination.MaskedIban);

            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var command = new NpgsqlCommand("""
                SELECT masked_iban, encrypted_iban FROM courier_payout_destinations;
                """, connection))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal("IR********************9012", reader.GetString(0));
                Assert.DoesNotContain(iban, reader.GetString(1), StringComparison.Ordinal);
            }

            var destinationRead = await withdrawals.ReadDestinationForOperationsAsync(
                courierId, now.AddMinutes(1), CancellationToken.None);
            Assert.Equal("ACCESS_GRANTED", destinationRead.OutcomeCode);
            Assert.Equal(iban, destinationRead.Iban);
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var audit = new NpgsqlCommand("""
                SELECT outcome_code, actor_id, destination_id
                FROM courier_payout_destination_access_audit;
                """, connection))
            await using (var reader = await audit.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal("ACCESS_GRANTED", reader.GetString(0));
                Assert.Equal(operations.ActorId, reader.GetString(1));
                Assert.False(reader.IsDBNull(2));
            }

            var concurrentKeys = new[] { "withdraw-001-a", "withdraw-001-b" };
            var concurrentRequests = await Task.WhenAll(concurrentKeys.Select(key =>
                withdrawals.CreateWithdrawalAsync(courierId, 30_000, key, now, CancellationToken.None)));
            var acceptedIndex = Array.FindIndex(concurrentRequests,
                result => result.OutcomeCode == "REQUESTED");
            Assert.InRange(acceptedIndex, 0, 1);
            var rejectedIndex = 1 - acceptedIndex;
            Assert.Equal("INSUFFICIENT_AVAILABLE_BALANCE", concurrentRequests[rejectedIndex].OutcomeCode);
            var successfulKey = concurrentKeys[acceptedIndex];
            var request = concurrentRequests[acceptedIndex];
            Assert.NotNull(request.Withdrawal);
            Assert.Equal("IR********************9012", request.Withdrawal.MaskedIban);

            var ledger = new PostgresCourierEarningLedger(dataSource);
            var reservedBalance = await ledger.GetAvailableBalanceAsync(courierId, now, CancellationToken.None);
            Assert.Equal(44_500L, reservedBalance.PostedAmountToman);
            Assert.Equal(30_000L, reservedBalance.ReservedWithdrawalAmountToman);
            Assert.Equal(14_500L, reservedBalance.AvailableAmountToman);

            var replay = await withdrawals.CreateWithdrawalAsync(courierId, 30_000,
                successfulKey, now.AddMinutes(1), CancellationToken.None);
            Assert.True(replay.AlreadyRequested);
            Assert.Equal(request.Withdrawal!.WithdrawalId, replay.Withdrawal!.WithdrawalId);
            var conflict = await withdrawals.CreateWithdrawalAsync(courierId, 29_000,
                successfulKey, now.AddMinutes(1), CancellationToken.None);
            Assert.Equal("IDEMPOTENCY_CONFLICT", conflict.OutcomeCode);

            var processing = await withdrawals.TransitionAsync(request.Withdrawal.WithdrawalId,
                "PROCESSING", null, null, "process-001", now.AddMinutes(2), CancellationToken.None);
            Assert.Equal("PROCESSING", processing.OutcomeCode);
            var paid = await withdrawals.TransitionAsync(request.Withdrawal.WithdrawalId,
                "PAID", null, "bank:trace-001", "pay-001", now.AddMinutes(3), CancellationToken.None);
            Assert.Equal("PAID", paid.OutcomeCode);
            var paidBalance = await ledger.GetAvailableBalanceAsync(courierId, now, CancellationToken.None);
            Assert.Equal(14_500L, paidBalance.PostedAmountToman);
            Assert.Equal(0L, paidBalance.ReservedWithdrawalAmountToman);
            Assert.Equal(14_500L, paidBalance.AvailableAmountToman);

            var ledgerPage = await ledger.GetLedgerHistoryAsync(courierId, 1, null, CancellationToken.None);
            Assert.Single(ledgerPage.Items);
            Assert.Equal("WITHDRAWAL_PAYOUT", ledgerPage.Items[0].ReferenceType);
            Assert.Equal(request.Withdrawal.WithdrawalId, ledgerPage.Items[0].ReferenceId);
            Assert.Equal(30_000L, ledgerPage.Items[0].DebitAmountToman);
            Assert.Equal(0L, ledgerPage.Items[0].CreditAmountToman);
            Assert.Equal(30_000L, ledgerPage.TotalDebitAmountToman);
            Assert.Equal(44_500L, ledgerPage.TotalCreditAmountToman);
            Assert.Equal(0L, ledgerPage.OpeningPostedBalanceToman);
            Assert.Equal(14_500L, ledgerPage.ClosingPostedBalanceToman);
            Assert.NotNull(ledgerPage.NextBeforeEntryId);

            var rangedLedgerPage1 = await ledger.GetLedgerHistoryAsync(
                courierId, 1, null, CancellationToken.None, now, now.AddMinutes(4));
            Assert.Single(rangedLedgerPage1.Items);
            Assert.Equal("WITHDRAWAL_PAYOUT", rangedLedgerPage1.Items[0].ReferenceType);
            Assert.Equal(30_000L, rangedLedgerPage1.TotalDebitAmountToman);
            Assert.Equal(44_500L, rangedLedgerPage1.TotalCreditAmountToman);
            Assert.Equal(0L, rangedLedgerPage1.OpeningPostedBalanceToman);
            Assert.Equal(14_500L, rangedLedgerPage1.ClosingPostedBalanceToman);
            Assert.NotNull(rangedLedgerPage1.NextBeforeEntryId);
            var rangedLedgerPage2 = await ledger.GetLedgerHistoryAsync(
                courierId, 1, rangedLedgerPage1.NextBeforeEntryId, CancellationToken.None,
                now, now.AddMinutes(4));
            Assert.Single(rangedLedgerPage2.Items);
            Assert.Equal("MISSION_EARNING", rangedLedgerPage2.Items[0].ReferenceType);
            Assert.Equal(rangedLedgerPage1.TotalDebitAmountToman, rangedLedgerPage2.TotalDebitAmountToman);
            Assert.Equal(rangedLedgerPage1.TotalCreditAmountToman, rangedLedgerPage2.TotalCreditAmountToman);
            Assert.Equal(rangedLedgerPage1.OpeningPostedBalanceToman, rangedLedgerPage2.OpeningPostedBalanceToman);
            Assert.Equal(rangedLedgerPage1.ClosingPostedBalanceToman, rangedLedgerPage2.ClosingPostedBalanceToman);
            Assert.Null(rangedLedgerPage2.NextBeforeEntryId);

            var payoutWindow = await ledger.GetLedgerHistoryAsync(
                courierId, 50, null, CancellationToken.None, now.AddMinutes(1), now.AddMinutes(4));
            Assert.Single(payoutWindow.Items);
            Assert.Equal(30_000L, payoutWindow.TotalDebitAmountToman);
            Assert.Equal(0L, payoutWindow.TotalCreditAmountToman);
            Assert.Equal(44_500L, payoutWindow.OpeningPostedBalanceToman);
            Assert.Equal(14_500L, payoutWindow.ClosingPostedBalanceToman);
            var earlierLedgerPage = await ledger.GetLedgerHistoryAsync(
                courierId, 1, ledgerPage.NextBeforeEntryId, CancellationToken.None);
            Assert.Single(earlierLedgerPage.Items);
            Assert.Equal("MISSION_EARNING", earlierLedgerPage.Items[0].ReferenceType);
            Assert.Equal(missionId, earlierLedgerPage.Items[0].ReferenceId);
            Assert.Equal(44_500L, earlierLedgerPage.Items[0].CreditAmountToman);
            Assert.Null(earlierLedgerPage.NextBeforeEntryId);

            var second = await withdrawals.CreateWithdrawalAsync(courierId, 10_000,
                "withdraw-002", now.AddMinutes(4), CancellationToken.None);
            Assert.Equal("REQUESTED", second.OutcomeCode);
            var releasedBalance = await ledger.GetAvailableBalanceAsync(courierId, now, CancellationToken.None);
            Assert.Equal(14_500L, releasedBalance.PostedAmountToman);
            Assert.Equal(10_000L, releasedBalance.ReservedWithdrawalAmountToman);
            Assert.Equal(4_500L, releasedBalance.AvailableAmountToman);

            var secondProcessing = await withdrawals.TransitionAsync(second.Withdrawal!.WithdrawalId,
                "PROCESSING", null, null, "process-002", now.AddMinutes(5), CancellationToken.None);
            Assert.Equal("PROCESSING", secondProcessing.OutcomeCode);

            // A balanced payout with the wrong amount must fail the deferred database invariant.
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var invalidPayout = await connection.BeginTransactionAsync())
            {
                var nowPayout = now.AddMinutes(6);
                await using (var eventCommand = new NpgsqlCommand("""
                    INSERT INTO courier_withdrawal_events
                        (withdrawal_id, action, actor_type, actor_id, idempotency_key, request_hash,
                         reason_code, bank_reference, occurred_at)
                    VALUES (@withdrawal_id, 'PAID', 'OPERATIONS', 'ops_test_actor',
                            'forged-pay-002', repeat('a', 64), NULL, 'bank:forged-002', @occurred_at);
                    """, connection, invalidPayout))
                {
                    eventCommand.Parameters.AddWithValue("withdrawal_id", second.Withdrawal.WithdrawalId);
                    eventCommand.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, nowPayout);
                    await eventCommand.ExecuteNonQueryAsync();
                }

                var invalidTransactionId = "ltx_" + Guid.NewGuid().ToString("N");
                await using (var transactionCommand = new NpgsqlCommand("""
                    INSERT INTO ledger_transactions
                        (ledger_transaction_id, reference_type, reference_id, idempotency_key,
                         currency, occurred_at, created_at)
                    VALUES (@transaction_id, 'WITHDRAWAL_PAYOUT', @withdrawal_id,
                            'forged-payout:' || @withdrawal_id, 'TOMAN', @occurred_at, @created_at);
                    """, connection, invalidPayout))
                {
                    transactionCommand.Parameters.AddWithValue("transaction_id", invalidTransactionId);
                    transactionCommand.Parameters.AddWithValue("withdrawal_id", second.Withdrawal.WithdrawalId);
                    transactionCommand.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, nowPayout);
                    transactionCommand.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, nowPayout);
                    await transactionCommand.ExecuteNonQueryAsync();
                }

                await using (var courierEntry = new NpgsqlCommand("""
                    INSERT INTO ledger_entries
                        (ledger_transaction_id, ledger_account_id, debit_amount, credit_amount, created_at)
                    SELECT @transaction_id, ledger_account_id, 9_999, 0, @created_at
                    FROM ledger_accounts
                    WHERE owner_type = 'COURIER' AND owner_id = @courier_id
                      AND account_code = 'COURIER_AVAILABLE' AND currency = 'TOMAN';
                    """, connection, invalidPayout))
                {
                    courierEntry.Parameters.AddWithValue("transaction_id", invalidTransactionId);
                    courierEntry.Parameters.AddWithValue("courier_id", courierId);
                    courierEntry.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, nowPayout);
                    Assert.Equal(1, await courierEntry.ExecuteNonQueryAsync());
                }

                await using (var clearingEntry = new NpgsqlCommand("""
                    INSERT INTO ledger_entries
                        (ledger_transaction_id, ledger_account_id, debit_amount, credit_amount, created_at)
                    SELECT @transaction_id, ledger_account_id, 0, 9_999, @created_at
                    FROM ledger_accounts
                    WHERE owner_type = 'MONO' AND owner_id = 'mono'
                      AND account_code = 'MONO_PAYOUT_CLEARING' AND currency = 'TOMAN';
                    """, connection, invalidPayout))
                {
                    clearingEntry.Parameters.AddWithValue("transaction_id", invalidTransactionId);
                    clearingEntry.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, nowPayout);
                    Assert.Equal(1, await clearingEntry.ExecuteNonQueryAsync());
                }

                await Assert.ThrowsAsync<PostgresException>(async () =>
                    await invalidPayout.CommitAsync());
            }

            var rejected = await withdrawals.TransitionAsync(second.Withdrawal.WithdrawalId,
                "REJECTED", "MANUAL_REVIEW_REJECTED", null, "reject-002",
                now.AddMinutes(7), CancellationToken.None);
            Assert.Equal("REJECTED", rejected.OutcomeCode);
            var restoredBalance = await ledger.GetAvailableBalanceAsync(courierId, now, CancellationToken.None);
            Assert.Equal(14_500L, restoredBalance.PostedAmountToman);
            Assert.Equal(0L, restoredBalance.ReservedWithdrawalAmountToman);
            Assert.Equal(14_500L, restoredBalance.AvailableAmountToman);

            var third = await withdrawals.CreateWithdrawalAsync(courierId, 1_000,
                "withdraw-003", now.AddMinutes(6), CancellationToken.None);
            Assert.Equal("REQUESTED", third.OutcomeCode);
            var dailyLimit = await withdrawals.CreateWithdrawalAsync(courierId, 1_000,
                "withdraw-004", now.AddMinutes(7), CancellationToken.None);
            Assert.Equal("DAILY_LIMIT_REACHED", dailyLimit.OutcomeCode);
            var todayQuota = await withdrawals.GetDailyWithdrawalQuotaAsync(
                courierId, now.AddMinutes(7), CancellationToken.None);
            Assert.Equal(3L, todayQuota.DailyLimit);
            Assert.Equal(3L, todayQuota.UsedToday);
            Assert.Equal(0L, todayQuota.RemainingToday);

            var historyReader = new PostgresCourierWithdrawalHistoryReadService(dataSource, operations);
            var paidHistory = await historyReader.ReadAsync(
                request.Withdrawal.WithdrawalId, now.AddMinutes(8), CancellationToken.None);
            Assert.Equal("FOUND", paidHistory.OutcomeCode);
            Assert.Equal(new[] { "REQUESTED", "PROCESSING", "PAID" },
                paidHistory.Events.Select(item => item.Action));
            Assert.Equal("COURIER", paidHistory.Events[0].ActorType);
            Assert.Equal(courierId, paidHistory.Events[0].ActorId);
            Assert.All(paidHistory.Events.Skip(1), item =>
            {
                Assert.Equal("OPERATIONS", item.ActorType);
                Assert.Equal(operations.ActorId, item.ActorId);
            });
            Assert.Equal("bank:trace-001", paidHistory.Events[^1].BankReference);
            Assert.Equal("pay-001", paidHistory.Events[^1].IdempotencyKey);

            var courierTimeline = await historyReader.ReadForCourierAsync(
                courierId, request.Withdrawal.WithdrawalId, CancellationToken.None);
            Assert.Equal("FOUND", courierTimeline.OutcomeCode);
            Assert.Equal(new[] { "REQUESTED", "PROCESSING", "PAID" },
                courierTimeline.Events.Select(item => item.Action));
            Assert.Equal("bank:trace-001", courierTimeline.Events[^1].BankReference);
            Assert.Null(courierTimeline.Events[0].ReasonCode);
            Assert.Null(courierTimeline.Events[0].BankReference);

            var otherCourierTimeline = await historyReader.ReadForCourierAsync(
                "crr_22222222222222222222222222222222",
                request.Withdrawal.WithdrawalId, CancellationToken.None);
            Assert.Equal("NOT_FOUND", otherCourierTimeline.OutcomeCode);
            Assert.Empty(otherCourierTimeline.Events);

            const string missingWithdrawalId = "wdr_99999999999999999999999999999999";
            var missingHistory = await historyReader.ReadAsync(
                missingWithdrawalId, now.AddMinutes(9), CancellationToken.None);
            Assert.Equal("NOT_FOUND", missingHistory.OutcomeCode);
            Assert.Empty(missingHistory.Events);

            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var audit = new NpgsqlCommand("""
                SELECT outcome_code, event_count, actor_id
                FROM courier_withdrawal_history_read_audit
                ORDER BY occurred_at, read_audit_id;
                """, connection))
            await using (var reader = await audit.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal("FOUND", reader.GetString(0));
                Assert.Equal(3, reader.GetInt32(1));
                Assert.Equal(operations.ActorId, reader.GetString(2));
                Assert.True(await reader.ReadAsync());
                Assert.Equal("NOT_FOUND", reader.GetString(0));
                Assert.Equal(0, reader.GetInt32(1));
                Assert.Equal(operations.ActorId, reader.GetString(2));
                Assert.False(await reader.ReadAsync());
            }

            var destinationAuditReader = new PostgresCourierPayoutDestinationAuditReadService(dataSource, operations);
            var firstAuditPage = await destinationAuditReader.ReadAsync(
                courierId, 1, null, now.AddMinutes(10), CancellationToken.None);
            Assert.Equal("FOUND", firstAuditPage.OutcomeCode);
            Assert.Single(firstAuditPage.Items);
            Assert.Equal("SECRET_ACCESS", firstAuditPage.Items[0].EventType);
            Assert.Equal("ACCESS_GRANTED", firstAuditPage.Items[0].OutcomeCode);
            Assert.Equal("IR********************9012", firstAuditPage.Items[0].MaskedIban);
            Assert.NotNull(firstAuditPage.NextCursor);

            var secondAuditPage = await destinationAuditReader.ReadAsync(
                courierId, 1, firstAuditPage.NextCursor, now.AddMinutes(11), CancellationToken.None);
            Assert.Equal("FOUND", secondAuditPage.OutcomeCode);
            Assert.Single(secondAuditPage.Items);
            Assert.Equal("DESTINATION", secondAuditPage.Items[0].EventType);
            Assert.Equal("VERIFIED", secondAuditPage.Items[0].Action);
            Assert.Equal("case:bank-proof-001", secondAuditPage.Items[0].EvidenceReference);
            Assert.Equal("IR********************9012", secondAuditPage.Items[0].MaskedIban);
            Assert.Null(secondAuditPage.NextCursor);

            var emptyCourierAudit = await destinationAuditReader.ReadAsync(
                "crr_22222222222222222222222222222222", 50, null, now.AddMinutes(12), CancellationToken.None);
            Assert.Equal("NOT_FOUND", emptyCourierAudit.OutcomeCode);
            Assert.Empty(emptyCourierAudit.Items);

            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var audit = new NpgsqlCommand("""
                SELECT outcome_code, result_count, actor_id
                FROM courier_payout_destination_history_read_audit
                ORDER BY occurred_at, read_audit_id;
                """, connection))
            await using (var reader = await audit.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal("FOUND", reader.GetString(0));
                Assert.Equal(1, reader.GetInt32(1));
                Assert.Equal(operations.ActorId, reader.GetString(2));
                Assert.True(await reader.ReadAsync());
                Assert.Equal("FOUND", reader.GetString(0));
                Assert.Equal(1, reader.GetInt32(1));
                Assert.Equal(operations.ActorId, reader.GetString(2));
                Assert.True(await reader.ReadAsync());
                Assert.Equal("NOT_FOUND", reader.GetString(0));
                Assert.Equal(0, reader.GetInt32(1));
                Assert.Equal(operations.ActorId, reader.GetString(2));
                Assert.False(await reader.ReadAsync());
            }


            var destinationSearch = new PostgresCourierPayoutDestinationAuditSearchService(dataSource, operations);
            var destinationSearchFrom = now.AddMinutes(-30);
            var destinationSearchTo = now.AddMinutes(30);
            var destinationSearchPage1 = await destinationSearch.SearchAsync(
                1, courierId, null, null, null, destinationSearchFrom, destinationSearchTo,
                null, now.AddMinutes(13), CancellationToken.None);
            Assert.Single(destinationSearchPage1.Items);
            Assert.NotNull(destinationSearchPage1.NextCursor);
            var destinationSearchPage2 = await destinationSearch.SearchAsync(
                1, courierId, null, null, null, destinationSearchFrom, destinationSearchTo,
                destinationSearchPage1.NextCursor, now.AddMinutes(14), CancellationToken.None);
            Assert.Single(destinationSearchPage2.Items);
            Assert.All(destinationSearchPage1.Items.Concat(destinationSearchPage2.Items), item =>
            {
                Assert.Contains(item.EventType, new[] { "DESTINATION", "SECRET_ACCESS" });
                Assert.True(item.MaskedIban is null ||
                    item.MaskedIban.StartsWith("IR********************", StringComparison.Ordinal));
            });
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var audit = new NpgsqlCommand("""
                SELECT count(*), count(*) FILTER (
                    WHERE actor_id = @actor AND courier_id_filter = @courier_id
                      AND occurred_from_filter = @from_at AND occurred_to_filter = @to_at
                      AND result_count = 1 AND outcome_code = 'FOUND'),
                    count(*) FILTER (WHERE cursor_event_key IS NOT NULL)
                FROM courier_payout_destination_operations_search_audit;
                """, connection))
            {
                audit.Parameters.AddWithValue("actor", operations.ActorId);
                audit.Parameters.AddWithValue("courier_id", courierId);
                audit.Parameters.AddWithValue("from_at", destinationSearchFrom);
                audit.Parameters.AddWithValue("to_at", destinationSearchTo);
                await using var reader = await audit.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(2L, reader.GetInt64(1));
                Assert.Equal(1L, reader.GetInt64(2));
            }

            await using (var indexConnection = await dataSource.OpenConnectionAsync())
            await using (var indexCheck = new NpgsqlCommand("""
                SELECT indexdef
                FROM pg_indexes
                WHERE schemaname = current_schema()
                  AND indexname = 'ledger_transactions_withdrawal_payout_reference_idx';
                """, indexConnection))
            {
                var indexDefinition = (string?)await indexCheck.ExecuteScalarAsync();
                Assert.NotNull(indexDefinition);
                Assert.Contains("reference_id", indexDefinition);
                Assert.Contains("WITHDRAWAL_PAYOUT", indexDefinition);
            }

            var reconciliation = new PostgresCourierPayoutReconciliationService(dataSource, operations);
            var reconciliationFrom = now.AddMinutes(-30);
            var reconciliationTo = now.AddMinutes(90);
            var reconciled = new List<CourierPayoutReconciliationItem>();
            CourierPayoutReconciliationCursor? reconciliationCursor = null;
            var reconciliationPages = 0;
            do
            {
                var reconciliationPage = await reconciliation.ReadAsync(
                    2, courierId, null, reconciliationFrom, reconciliationTo, false,
                    reconciliationCursor, now.AddMinutes(30 + reconciliationPages), CancellationToken.None);
                reconciliationPages++;
                reconciled.AddRange(reconciliationPage.Items);
                reconciliationCursor = reconciliationPage.NextCursor;
            } while (reconciliationCursor is not null);
            Assert.Contains(reconciled, item =>
                item.WithdrawalId == request.Withdrawal!.WithdrawalId &&
                item.LatestAction == "PAID" &&
                item.ReconciliationCode == "MATCHED" &&
                item.PayoutTransactionCount == 1 &&
                item.LedgerEntryCount == 2 &&
                item.MatchingCourierDebitCount == 1 &&
                item.MatchingClearingCreditCount == 1);
            Assert.All(reconciled, item => Assert.NotEqual("LEDGER_MISMATCH", item.ReconciliationCode));
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var audit = new NpgsqlCommand("""
                SELECT count(*), count(*) FILTER (
                    WHERE actor_id = @actor AND courier_id_filter = @courier_id
                      AND created_from_filter = @from_at AND created_to_filter = @to_at
                      AND only_discrepancies = false AND outcome_code = 'FOUND'),
                    count(*) FILTER (WHERE cursor_withdrawal_id IS NOT NULL)
                FROM courier_payout_reconciliation_read_audit;
                """, connection))
            {
                audit.Parameters.AddWithValue("actor", operations.ActorId);
                audit.Parameters.AddWithValue("courier_id", courierId);
                audit.Parameters.AddWithValue("from_at", reconciliationFrom);
                audit.Parameters.AddWithValue("to_at", reconciliationTo);
                await using var reader = await audit.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal((long)reconciliationPages, reader.GetInt64(0));
                Assert.Equal((long)reconciliationPages, reader.GetInt64(1));
                Assert.Equal((long)(reconciliationPages - 1), reader.GetInt64(2));
            }

            var ledgerDetailReader = new PostgresCourierPayoutLedgerDetailReadService(dataSource, operations);
            var payoutLedgerDetail = await ledgerDetailReader.ReadAsync(
                request.Withdrawal!.WithdrawalId, now.AddMinutes(31), CancellationToken.None);
            Assert.Equal("FOUND", payoutLedgerDetail.OutcomeCode);
            Assert.Equal("PAID", payoutLedgerDetail.LatestAction);
            Assert.Single(payoutLedgerDetail.Transactions);
            Assert.Equal(2, payoutLedgerDetail.Transactions[0].Entries.Count);
            Assert.Contains(payoutLedgerDetail.Transactions[0].Entries, entry =>
                entry.AccountCode == "COURIER_AVAILABLE" && entry.AccountOwnerType == "COURIER" &&
                entry.DebitAmountToman == request.Withdrawal.AmountToman && entry.CreditAmountToman == 0);
            Assert.Contains(payoutLedgerDetail.Transactions[0].Entries, entry =>
                entry.AccountCode == "MONO_PAYOUT_CLEARING" && entry.AccountOwnerType == "MONO" &&
                entry.DebitAmountToman == 0 && entry.CreditAmountToman == request.Withdrawal.AmountToman);
            const string missingLedgerWithdrawalId = "wdr_88888888888888888888888888888888";
            var missingLedgerDetail = await ledgerDetailReader.ReadAsync(missingLedgerWithdrawalId,
                now.AddMinutes(32), CancellationToken.None);
            Assert.Equal("WITHDRAWAL_NOT_FOUND", missingLedgerDetail.OutcomeCode);
            Assert.Empty(missingLedgerDetail.Transactions);
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var audit = new NpgsqlCommand("""
                SELECT count(*), count(*) FILTER (WHERE actor_id = @actor AND withdrawal_id = @withdrawal_id
                    AND outcome_code = 'FOUND' AND transaction_count = 1 AND entry_count = 2),
                    count(*) FILTER (WHERE actor_id = @actor AND withdrawal_id = @missing_id
                    AND outcome_code = 'WITHDRAWAL_NOT_FOUND' AND transaction_count = 0 AND entry_count = 0)
                FROM courier_payout_ledger_detail_read_audit;
                """, connection))
            {
                audit.Parameters.AddWithValue("actor", operations.ActorId);
                audit.Parameters.AddWithValue("withdrawal_id", request.Withdrawal.WithdrawalId);
                audit.Parameters.AddWithValue("missing_id", missingLedgerWithdrawalId);
                await using var reader = await audit.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
                Assert.Equal(1L, reader.GetInt64(2));
            }

            var detailAuditSearch = new PostgresCourierPayoutLedgerDetailAuditSearchService(dataSource, operations);
            var detailAuditFrom = now.AddMinutes(30);
            var detailAuditTo = now.AddMinutes(34);
            var detailAuditPage1 = await detailAuditSearch.ReadAsync(
                1, operations.ActorId, null, null, detailAuditFrom, detailAuditTo,
                null, now.AddMinutes(33), CancellationToken.None);
            Assert.Single(detailAuditPage1.Items);
            Assert.Equal("WITHDRAWAL_NOT_FOUND", detailAuditPage1.Items[0].OutcomeCode);
            Assert.NotNull(detailAuditPage1.NextCursor);
            var detailAuditPage2 = await detailAuditSearch.ReadAsync(
                1, operations.ActorId, null, null, detailAuditFrom, detailAuditTo,
                detailAuditPage1.NextCursor, now.AddMinutes(34), CancellationToken.None);
            Assert.Single(detailAuditPage2.Items);
            Assert.Equal(request.Withdrawal.WithdrawalId, detailAuditPage2.Items[0].WithdrawalId);
            Assert.Equal("FOUND", detailAuditPage2.Items[0].OutcomeCode);
            Assert.Null(detailAuditPage2.NextCursor);
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var audit = new NpgsqlCommand("""
                SELECT count(*), count(*) FILTER (
                    WHERE actor_id = @actor AND actor_filter = @actor
                      AND occurred_from_filter = @from_at AND occurred_to_filter = @to_at
                      AND result_count = 1 AND outcome_code = 'FOUND'),
                    count(*) FILTER (WHERE cursor_occurred_at IS NOT NULL)
                FROM courier_payout_ledger_detail_search_read_audit;
                """, connection))
            {
                audit.Parameters.AddWithValue("actor", operations.ActorId);
                audit.Parameters.AddWithValue("from_at", detailAuditFrom);
                audit.Parameters.AddWithValue("to_at", detailAuditTo);
                await using var reader = await audit.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(2L, reader.GetInt64(1));
                Assert.Equal(1L, reader.GetInt64(2));
            }

            var reconciliationSummaryService = new PostgresCourierPayoutReconciliationSummaryService(dataSource, operations);
            var reconciliationSummary = await reconciliationSummaryService.ReadAsync(
                courierId, null, reconciliationFrom, reconciliationTo, now.AddMinutes(35), CancellationToken.None);
            Assert.Equal((long)reconciled.Count, reconciliationSummary.TotalWithdrawalCount);
            Assert.Equal((long)reconciled.Count(item => item.ReconciliationCode != "MATCHED"),
                reconciliationSummary.DiscrepancyCount);
            Assert.Equal(reconciled.Sum(item => item.AmountToman), reconciliationSummary.TotalAmountToman);
            Assert.Equal(reconciled.GroupBy(item => item.ReconciliationCode).Count(),
                reconciliationSummary.Breakdown.Count);
            Assert.All(reconciliationSummary.Breakdown, item =>
            {
                var expected = reconciled.Where(row => row.ReconciliationCode == item.ReconciliationCode).ToArray();
                Assert.Equal((long)expected.Length, item.WithdrawalCount);
                Assert.Equal(expected.Sum(row => row.AmountToman), item.AmountToman);
            });
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var audit = new NpgsqlCommand("""
                SELECT count(*), count(*) FILTER (
                    WHERE actor_id = @actor AND courier_id_filter = @courier_id
                      AND status_filter IS NULL AND created_from_filter = @from_at
                      AND created_to_filter = @to_at AND total_withdrawal_count = @total
                      AND discrepancy_count = @discrepancies AND outcome_code = 'FOUND')
                FROM courier_payout_reconciliation_summary_read_audit;
                """, connection))
            {
                audit.Parameters.AddWithValue("actor", operations.ActorId);
                audit.Parameters.AddWithValue("courier_id", courierId);
                audit.Parameters.AddWithValue("from_at", reconciliationFrom);
                audit.Parameters.AddWithValue("to_at", reconciliationTo);
                audit.Parameters.AddWithValue("total", (long)reconciled.Count);
                audit.Parameters.AddWithValue("discrepancies", (long)reconciled.Count(item => item.ReconciliationCode != "MATCHED"));
                await using var reader = await audit.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
            }

            var reconciliationTrendService = new PostgresCourierPayoutReconciliationTrendService(dataSource, operations);
            var reconciliationTrend = await reconciliationTrendService.ReadAsync(
                courierId, null, reconciliationFrom, reconciliationTo, now.AddMinutes(36), CancellationToken.None);
            Assert.Equal((long)reconciled.Count, reconciliationTrend.TotalWithdrawalCount);
            Assert.Equal(reconciled.Sum(item => item.AmountToman), reconciliationTrend.TotalAmountToman);
            Assert.Equal((long)reconciled.Count(item => item.ReconciliationCode != "MATCHED"),
                reconciliationTrend.DiscrepancyCount);
            var expectedTrend = reconciled.GroupBy(item => new
            {
                Day = DateOnly.FromDateTime(item.CreatedAt.UtcDateTime),
                item.ReconciliationCode
            }).ToDictionary(group => (group.Key.Day, group.Key.ReconciliationCode), group => group.ToArray());
            Assert.Equal(expectedTrend.Count, reconciliationTrend.Buckets.Count);
            foreach (var bucket in reconciliationTrend.Buckets)
            {
                var expected = expectedTrend[(bucket.UtcDate, bucket.ReconciliationCode)];
                Assert.Equal((long)expected.Length, bucket.WithdrawalCount);
                Assert.Equal(expected.Sum(item => item.AmountToman), bucket.AmountToman);
            }
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var audit = new NpgsqlCommand("""
                SELECT count(*), count(*) FILTER (
                    WHERE actor_id = @actor AND courier_id_filter = @courier_id
                      AND status_filter IS NULL AND created_from_filter = @from_at
                      AND created_to_filter = @to_at AND total_withdrawal_count = @total
                      AND bucket_count = @buckets AND outcome_code = 'FOUND')
                FROM courier_payout_reconciliation_trend_read_audit;
                """, connection))
            {
                audit.Parameters.AddWithValue("actor", operations.ActorId);
                audit.Parameters.AddWithValue("courier_id", courierId);
                audit.Parameters.AddWithValue("from_at", reconciliationFrom);
                audit.Parameters.AddWithValue("to_at", reconciliationTo);
                audit.Parameters.AddWithValue("total", (long)reconciled.Count);
                audit.Parameters.AddWithValue("buckets", reconciliationTrend.Buckets.Count);
                await using var reader = await audit.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
            }

            var fromAt = now.AddMinutes(2);
            var toAt = now.AddMinutes(7);
            var courierWithdrawalPage1 = await withdrawals.ListForCourierAsync(
                courierId, 1, null, CancellationToken.None, fromAt, toAt);
            Assert.Single(courierWithdrawalPage1.Items);
            Assert.Equal(third.Withdrawal!.WithdrawalId, courierWithdrawalPage1.Items[0].WithdrawalId);
            Assert.NotNull(courierWithdrawalPage1.NextBeforeSequence);
            var courierWithdrawalPage2 = await withdrawals.ListForCourierAsync(
                courierId, 1, courierWithdrawalPage1.NextBeforeSequence, CancellationToken.None, fromAt, toAt);
            Assert.Single(courierWithdrawalPage2.Items);
            Assert.Equal(second.Withdrawal.WithdrawalId, courierWithdrawalPage2.Items[0].WithdrawalId);
            Assert.Null(courierWithdrawalPage2.NextBeforeSequence);
            var requestedCourierWithdrawals = await withdrawals.ListForCourierAsync(
                courierId, 50, null, CancellationToken.None, fromAt, toAt, "REQUESTED");
            Assert.Single(requestedCourierWithdrawals.Items);
            Assert.Equal(third.Withdrawal!.WithdrawalId, requestedCourierWithdrawals.Items[0].WithdrawalId);
            var rejectedCourierWithdrawals = await withdrawals.ListForCourierAsync(
                courierId, 50, null, CancellationToken.None, fromAt, toAt, "REJECTED");
            Assert.Single(rejectedCourierWithdrawals.Items);
            Assert.Equal(second.Withdrawal.WithdrawalId, rejectedCourierWithdrawals.Items[0].WithdrawalId);
            Assert.Equal(10_000L, rejectedCourierWithdrawals.Items[0].AmountToman);
            var exactAmountCourierWithdrawals = await withdrawals.ListForCourierAsync(
                courierId, 50, null, CancellationToken.None, fromAt, toAt, null, 1_000, 1_000);
            Assert.Single(exactAmountCourierWithdrawals.Items);
            Assert.Equal(third.Withdrawal!.WithdrawalId, exactAmountCourierWithdrawals.Items[0].WithdrawalId);
            var minimumAmountCourierWithdrawals = await withdrawals.ListForCourierAsync(
                courierId, 50, null, CancellationToken.None, fromAt, toAt, null, 10_000, null);
            Assert.Single(minimumAmountCourierWithdrawals.Items);
            Assert.Equal(second.Withdrawal.WithdrawalId, minimumAmountCourierWithdrawals.Items[0].WithdrawalId);
            var emptyCourierWithdrawalWindow = await withdrawals.ListForCourierAsync(
                courierId, 50, null, CancellationToken.None, now.AddMinutes(7), now.AddMinutes(9));
            Assert.Empty(emptyCourierWithdrawalWindow.Items);

            var operationsSearch = new PostgresCourierWithdrawalOperationsSearchService(dataSource, operations);
            var operationsPage1 = await operationsSearch.SearchAsync(
                null, null, 1, null, now.AddMinutes(13), CancellationToken.None);
            Assert.Single(operationsPage1.Items);
            Assert.Equal(third.Withdrawal!.WithdrawalId, operationsPage1.Items[0].WithdrawalId);
            Assert.Equal("REQUESTED", operationsPage1.Items[0].Status);
            Assert.NotNull(operationsPage1.NextBeforeSequence);

            var operationsPage2 = await operationsSearch.SearchAsync(
                null, null, 1, operationsPage1.NextBeforeSequence, now.AddMinutes(14), CancellationToken.None);
            Assert.Single(operationsPage2.Items);
            Assert.Equal(second.Withdrawal!.WithdrawalId, operationsPage2.Items[0].WithdrawalId);
            Assert.Equal("REJECTED", operationsPage2.Items[0].Status);
            Assert.NotNull(operationsPage2.NextBeforeSequence);

            var operationsPage3 = await operationsSearch.SearchAsync(
                null, null, 1, operationsPage2.NextBeforeSequence, now.AddMinutes(15), CancellationToken.None);
            Assert.Single(operationsPage3.Items);
            Assert.Equal(request.Withdrawal.WithdrawalId, operationsPage3.Items[0].WithdrawalId);
            Assert.Equal("PAID", operationsPage3.Items[0].Status);
            Assert.Null(operationsPage3.NextBeforeSequence);

            var paidOnly = await operationsSearch.SearchAsync(
                "PAID", courierId, 50, null, now.AddMinutes(16), CancellationToken.None);
            Assert.Single(paidOnly.Items);
            Assert.Equal(request.Withdrawal.WithdrawalId, paidOnly.Items[0].WithdrawalId);

            var operationsFromAt = now.AddMinutes(2);
            var operationsToAt = now.AddMinutes(7);
            var filteredOperationsPage1 = await operationsSearch.SearchAsync(
                null, courierId, 1, null, now.AddMinutes(17), CancellationToken.None,
                operationsFromAt, operationsToAt, 1_000, 10_000);
            Assert.Single(filteredOperationsPage1.Items);
            Assert.Equal(third.Withdrawal!.WithdrawalId, filteredOperationsPage1.Items[0].WithdrawalId);
            Assert.NotNull(filteredOperationsPage1.NextBeforeSequence);
            var filteredOperationsPage2 = await operationsSearch.SearchAsync(
                null, courierId, 1, filteredOperationsPage1.NextBeforeSequence,
                now.AddMinutes(18), CancellationToken.None,
                operationsFromAt, operationsToAt, 1_000, 10_000);
            Assert.Single(filteredOperationsPage2.Items);
            Assert.Equal(second.Withdrawal!.WithdrawalId, filteredOperationsPage2.Items[0].WithdrawalId);
            Assert.Null(filteredOperationsPage2.NextBeforeSequence);

            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var audit = new NpgsqlCommand("""
                SELECT count(*), count(*) FILTER (
                    WHERE status_filter = 'PAID' AND courier_id_filter = @courier_id AND result_count = 1),
                    count(*) FILTER (WHERE cursor_sequence IS NOT NULL),
                    count(*) FILTER (
                        WHERE courier_id_filter = @courier_id
                          AND created_from_filter = @created_from
                          AND created_to_filter = @created_to
                          AND min_amount_toman_filter = 1000
                          AND max_amount_toman_filter = 10000
                          AND result_count = 1)
                FROM courier_withdrawal_operations_search_audit;
                """, connection))
            {
                audit.Parameters.AddWithValue("courier_id", courierId);
                audit.Parameters.AddWithValue("created_from", operationsFromAt);
                audit.Parameters.AddWithValue("created_to", operationsToAt);
                await using var reader = await audit.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(6L, reader.GetInt64(0));
                Assert.Equal(1L, reader.GetInt64(1));
                Assert.Equal(3L, reader.GetInt64(2));
                Assert.Equal(2L, reader.GetInt64(3));
            }


            var auditReadService = new PostgresCourierWithdrawalOperationsAuditReadService(dataSource, operations);
            var searchAuditFrom = now.AddMinutes(17);
            var searchAuditTo = now.AddMinutes(19);
            var auditHistoryPage1 = await auditReadService.ReadAsync(
                1, operations.ActorId, null, courierId, searchAuditFrom, searchAuditTo,
                null, now.AddMinutes(20), CancellationToken.None);
            Assert.Single(auditHistoryPage1.Items);
            Assert.NotNull(auditHistoryPage1.Next);
            var auditHistoryPage2 = await auditReadService.ReadAsync(
                1, operations.ActorId, null, courierId, searchAuditFrom, searchAuditTo,
                auditHistoryPage1.Next, now.AddMinutes(21), CancellationToken.None);
            Assert.Single(auditHistoryPage2.Items);
            Assert.Null(auditHistoryPage2.Next);
            Assert.All(auditHistoryPage1.Items.Concat(auditHistoryPage2.Items), item =>
            {
                Assert.Equal(operations.ActorId, item.ActorId);
                Assert.Equal(courierId, item.CourierIdFilter);
                Assert.Equal(1, item.ResultCount);
            });
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var audit = new NpgsqlCommand("""
                SELECT count(*), count(*) FILTER (
                    WHERE actor_id = @actor AND actor_filter = @actor
                      AND courier_id_filter = @courier_id
                      AND occurred_from_filter = @from_at AND occurred_to_filter = @to_at
                      AND result_count = 1 AND outcome_code = 'FOUND'),
                    count(*) FILTER (WHERE cursor_search_audit_id IS NOT NULL)
                FROM courier_withdrawal_operations_audit_read;
                """, connection))
            {
                audit.Parameters.AddWithValue("actor", operations.ActorId);
                audit.Parameters.AddWithValue("courier_id", courierId);
                audit.Parameters.AddWithValue("from_at", searchAuditFrom);
                audit.Parameters.AddWithValue("to_at", searchAuditTo);
                await using var reader = await audit.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(2L, reader.GetInt64(1));
                Assert.Equal(1L, reader.GetInt64(2));
            }


            var eventSearch = new PostgresCourierWithdrawalOperationsEventSearchService(dataSource, operations);
            var eventFromAt = now.AddMinutes(-1);
            var eventToAt = now.AddMinutes(30);
            var eventPage1 = await eventSearch.SearchAsync(1, "REQUESTED", null, null, courierId,
                eventFromAt, eventToAt, null, now.AddMinutes(25), CancellationToken.None);
            Assert.Single(eventPage1.Items);
            Assert.Equal("REQUESTED", eventPage1.Items[0].Action);
            Assert.Equal(courierId, eventPage1.Items[0].CourierId);
            Assert.NotNull(eventPage1.NextSequence);
            var eventPage2 = await eventSearch.SearchAsync(1, "REQUESTED", null, null, courierId,
                eventFromAt, eventToAt, eventPage1.NextSequence, now.AddMinutes(26), CancellationToken.None);
            Assert.Single(eventPage2.Items);
            Assert.Equal("REQUESTED", eventPage2.Items[0].Action);
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var audit = new NpgsqlCommand("""
                SELECT count(*), count(*) FILTER (
                    WHERE actor_id = @actor AND action_filter = 'REQUESTED'
                      AND courier_id_filter = @courier_id
                      AND occurred_from_filter = @from_at AND occurred_to_filter = @to_at
                      AND result_count = 1 AND outcome_code = 'FOUND'),
                    count(*) FILTER (WHERE cursor_sequence IS NOT NULL)
                FROM courier_withdrawal_operations_event_search_audit
                WHERE action_filter = 'REQUESTED';
                """, connection))
            {
                audit.Parameters.AddWithValue("actor", operations.ActorId);
                audit.Parameters.AddWithValue("courier_id", courierId);
                audit.Parameters.AddWithValue("from_at", eventFromAt);
                audit.Parameters.AddWithValue("to_at", eventToAt);
                await using var reader = await audit.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(2L, reader.GetInt64(0));
                Assert.Equal(2L, reader.GetInt64(1));
                Assert.Equal(1L, reader.GetInt64(2));
            }

            var tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
            var nextLocalMidnight = DateTime.SpecifyKind(
                new DateTime(2026, 10, 17, 0, 0, 0), DateTimeKind.Unspecified);
            var nextTehranDayStart = new DateTimeOffset(
                TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, tehran));
            var boundaryOne = await withdrawals.CreateWithdrawalAsync(courierId, 1_000,
                "boundary-001", nextTehranDayStart, CancellationToken.None);
            var boundaryTwo = await withdrawals.CreateWithdrawalAsync(courierId, 1_000,
                "boundary-002", nextTehranDayStart.AddMinutes(1), CancellationToken.None);
            var boundaryThree = await withdrawals.CreateWithdrawalAsync(courierId, 1_000,
                "boundary-003", nextTehranDayStart.AddMinutes(2), CancellationToken.None);
            Assert.Equal("REQUESTED", boundaryOne.OutcomeCode);
            Assert.Equal("REQUESTED", boundaryTwo.OutcomeCode);
            Assert.Equal("REQUESTED", boundaryThree.OutcomeCode);
            var newDayQuota = await withdrawals.GetDailyWithdrawalQuotaAsync(
                courierId, nextTehranDayStart.AddMinutes(3), CancellationToken.None);
            Assert.Equal(3L, newDayQuota.DailyLimit);
            Assert.Equal(3L, newDayQuota.UsedToday);
            Assert.Equal(0L, newDayQuota.RemainingToday);
            var followingLocalMidnight = DateTime.SpecifyKind(
                new DateTime(2026, 10, 18, 0, 0, 0), DateTimeKind.Unspecified);
            var expectedReset = new DateTimeOffset(
                TimeZoneInfo.ConvertTimeToUtc(followingLocalMidnight, tehran));
            Assert.Equal(expectedReset, newDayQuota.ResetsAt);

            var boundaryReplay = await withdrawals.CreateWithdrawalAsync(courierId, 1_000,
                "boundary-001", nextTehranDayStart.AddMinutes(3), CancellationToken.None);
            Assert.True(boundaryReplay.AlreadyRequested);
            Assert.Equal(boundaryOne.Withdrawal!.WithdrawalId, boundaryReplay.Withdrawal!.WithdrawalId);
            var overBoundaryLimit = await withdrawals.CreateWithdrawalAsync(courierId, 1_000,
                "boundary-004", nextTehranDayStart.AddMinutes(4), CancellationToken.None);
            Assert.Equal("DAILY_LIMIT_REACHED", overBoundaryLimit.OutcomeCode);

            var revoke = await withdrawals.RevokeDestinationAsync(courierId,
                "case:bank-account-compromised", "destination-revoke-001",
                nextTehranDayStart.AddMinutes(5), CancellationToken.None);
            Assert.Equal("REVOKED", revoke.OutcomeCode);
            Assert.False(revoke.AlreadyRequested);
            var revokeReplay = await withdrawals.RevokeDestinationAsync(courierId,
                "case:bank-account-compromised", "destination-revoke-001",
                nextTehranDayStart.AddMinutes(6), CancellationToken.None);
            Assert.Equal("REVOKED", revokeReplay.OutcomeCode);
            Assert.True(revokeReplay.AlreadyRequested);
            var revokeConflict = await withdrawals.RevokeDestinationAsync(courierId,
                "case:other-evidence", "destination-revoke-001",
                nextTehranDayStart.AddMinutes(6), CancellationToken.None);
            Assert.Equal("IDEMPOTENCY_CONFLICT", revokeConflict.OutcomeCode);
            var revokedStatus = await withdrawals.GetDestinationStatusAsync(courierId, CancellationToken.None);
            Assert.Equal("NOT_VERIFIED", revokedStatus.OutcomeCode);
            Assert.Null(revokedStatus.MaskedIban);
            var withdrawalAfterRevocation = await withdrawals.CreateWithdrawalAsync(courierId, 1_000,
                "after-destination-revoke", nextTehranDayStart.AddMinutes(7), CancellationToken.None);
            Assert.Equal("PAYOUT_DESTINATION_UNVERIFIED", withdrawalAfterRevocation.OutcomeCode);
            var reverified = await withdrawals.VerifyDestinationAsync(courierId, iban,
                "case:bank-account-restored", "destination-reverify-001",
                nextTehranDayStart.AddMinutes(8), CancellationToken.None);
            Assert.Equal("VERIFIED", reverified.OutcomeCode);
            Assert.Equal("VERIFIED",
                (await withdrawals.GetDestinationStatusAsync(courierId, CancellationToken.None)).OutcomeCode);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(connectionString);
            await admin.OpenAsync();
            await using var dropSchema = new NpgsqlCommand($"DROP SCHEMA {schemaName} CASCADE;", admin);
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Payout_integrity_migration_fails_closed_on_legacy_mismatched_payout()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var schemaName = "legacy_payout_integrity_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(connectionString))
        {
            await admin.OpenAsync();
            await using var createSchema = new NpgsqlCommand($"CREATE SCHEMA {schemaName};", admin);
            await createSchema.ExecuteNonQueryAsync();
        }

        try
        {
            var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schemaName };
            await using var dataSource = NpgsqlDataSource.Create(scoped.ConnectionString);
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var setup = new NpgsqlCommand("""
                CREATE TABLE couriers (courier_id text PRIMARY KEY);
                CREATE TABLE missions (
                    mission_id text PRIMARY KEY, customer_id text NOT NULL,
                    delivery_request_id text NOT NULL, quote_id text NOT NULL,
                    quote_version integer NOT NULL, status text NOT NULL);
                CREATE TABLE delivery_quote_versions (
                    customer_id text NOT NULL, delivery_request_id text NOT NULL,
                    quote_id text NOT NULL, version integer NOT NULL, snapshot jsonb NOT NULL);
                CREATE TABLE mission_courier_offer_events (
                    event_sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                    mission_id text NOT NULL, customer_id text NOT NULL,
                    courier_id text NOT NULL, action text NOT NULL);
                INSERT INTO couriers VALUES ('crr_22222222222222222222222222222222');
                INSERT INTO missions VALUES
                    ('mis_22222222222222222222222222222222', 'cus_22222222222222222222222222222222',
                     'drq_2', 'quo_2', 1, 'DELIVERED');
                INSERT INTO delivery_quote_versions VALUES
                    ('cus_22222222222222222222222222222222', 'drq_2', 'quo_2', 1,
                     '{"fare":{"fare_toman":10000}}'::jsonb);
                INSERT INTO mission_courier_offer_events (mission_id, customer_id, courier_id, action)
                VALUES ('mis_22222222222222222222222222222222', 'cus_22222222222222222222222222222222',
                        'crr_22222222222222222222222222222222', 'COMPLETED');
                """, connection))
                await setup.ExecuteNonQueryAsync();

            await ApplyMigrationAsync(dataSource, includePayoutIntegrity: false);
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var transaction = await connection.BeginTransactionAsync())
            await using (var seed = new NpgsqlCommand("""
                INSERT INTO courier_payout_destinations
                    (destination_id, courier_id, encrypted_iban, iban_fingerprint, masked_iban, created_at)
                VALUES ('cpd_22222222222222222222222222222222',
                        'crr_22222222222222222222222222222222',
                        'v1:YWJj:ZGVm:Z2hp', repeat('a', 64), 'IR********************2222', TIMESTAMPTZ '2026-10-16 10:00:00+00');
                INSERT INTO courier_payout_destination_events
                    (destination_id, courier_id, action, actor_id, idempotency_key, request_hash,
                     evidence_reference, occurred_at)
                VALUES ('cpd_22222222222222222222222222222222',
                        'crr_22222222222222222222222222222222', 'VERIFIED', 'ops_legacy',
                        'legacy-verify', repeat('b', 64), 'case:legacy-proof', TIMESTAMPTZ '2026-10-16 10:00:00+00');
                INSERT INTO courier_withdrawal_requests
                    (withdrawal_id, courier_id, destination_id, amount_toman,
                     idempotency_key, request_hash, created_at)
                VALUES ('wdr_22222222222222222222222222222222',
                        'crr_22222222222222222222222222222222',
                        'cpd_22222222222222222222222222222222', 1000,
                        'legacy-request', repeat('c', 64), TIMESTAMPTZ '2026-10-16 10:00:00+00');
                INSERT INTO courier_withdrawal_events
                    (withdrawal_id, action, actor_type, actor_id, idempotency_key, request_hash, occurred_at)
                VALUES ('wdr_22222222222222222222222222222222', 'REQUESTED', 'COURIER',
                        'crr_22222222222222222222222222222222', 'legacy-request-event', repeat('d', 64), TIMESTAMPTZ '2026-10-16 10:00:00+00');
                INSERT INTO courier_withdrawal_events
                    (withdrawal_id, action, actor_type, actor_id, idempotency_key, request_hash, occurred_at)
                VALUES ('wdr_22222222222222222222222222222222', 'PROCESSING', 'OPERATIONS',
                        'ops_legacy', 'legacy-processing', repeat('e', 64), TIMESTAMPTZ '2026-10-16 10:00:00+00');
                INSERT INTO courier_withdrawal_events
                    (withdrawal_id, action, actor_type, actor_id, idempotency_key, request_hash,
                     bank_reference, occurred_at)
                VALUES ('wdr_22222222222222222222222222222222', 'PAID', 'OPERATIONS',
                        'ops_legacy', 'legacy-paid', repeat('f', 64), 'bank:legacy-222', TIMESTAMPTZ '2026-10-16 10:00:00+00');
                INSERT INTO ledger_accounts
                    (ledger_account_id, owner_type, owner_id, account_code, currency, created_at)
                VALUES ('lac_22222222222222222222222222222222', 'COURIER',
                        'crr_22222222222222222222222222222222', 'COURIER_AVAILABLE', 'TOMAN', TIMESTAMPTZ '2026-10-16 10:00:00+00'),
                       ('lac_33333333333333333333333333333333', 'MONO',
                        'mono', 'MONO_PAYOUT_CLEARING', 'TOMAN', TIMESTAMPTZ '2026-10-16 10:00:00+00');
                INSERT INTO ledger_transactions
                    (ledger_transaction_id, reference_type, reference_id, idempotency_key,
                     currency, occurred_at, created_at)
                VALUES ('ltx_22222222222222222222222222222222', 'WITHDRAWAL_PAYOUT',
                        'wdr_22222222222222222222222222222222', 'legacy-payout:222',
                        'TOMAN', TIMESTAMPTZ '2026-10-16 10:00:00+00', TIMESTAMPTZ '2026-10-16 10:00:00+00');
                INSERT INTO ledger_entries
                    (ledger_transaction_id, ledger_account_id, debit_amount, credit_amount, created_at)
                VALUES ('ltx_22222222222222222222222222222222',
                        'lac_22222222222222222222222222222222', 999, 0, TIMESTAMPTZ '2026-10-16 10:00:00+00'),
                       ('ltx_22222222222222222222222222222222',
                        'lac_33333333333333333333333333333333', 0, 999, TIMESTAMPTZ '2026-10-16 10:00:00+00');
                """, connection, transaction))
            {
                await seed.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }

            var exception = await Assert.ThrowsAsync<PostgresException>(
                () => ApplyMigrationAsync(dataSource));
            Assert.Contains("pre-existing payout ledger transaction violates courier payout integrity",
                exception.MessageText, StringComparison.Ordinal);
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var functionCheck = new NpgsqlCommand("""
                SELECT to_regprocedure(
                    'assert_existing_courier_payout_ledger_integrity()') IS NOT NULL;
                """, connection))
            {
                Assert.True((bool)(await functionCheck.ExecuteScalarAsync()
                    ?? throw new InvalidOperationException("Preflight function check returned no result.")));
            }
        }
        finally
        {
            await using var admin = new NpgsqlConnection(connectionString);
            await admin.OpenAsync();
            await using var dropSchema = new NpgsqlCommand($"DROP SCHEMA {schemaName} CASCADE;", admin);
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    private static async Task ApplyMigrationAsync(
        NpgsqlDataSource dataSource, bool includePayoutIntegrity = true)
    {
        var resources = new[]
        {
            "Mono.Api.Tests.Migrations.064_courier_earning_ledger.sql",
            "Mono.Api.Tests.Migrations.065_courier_manual_withdrawals.sql",
            "Mono.Api.Tests.Migrations.066_courier_withdrawal_history_read_audit.sql",
            "Mono.Api.Tests.Migrations.067_courier_payout_destination_history_read_audit.sql",
            "Mono.Api.Tests.Migrations.068_courier_withdrawal_operations_search_audit.sql",
            "Mono.Api.Tests.Migrations.069_courier_payout_ledger_integrity.sql",
            "Mono.Api.Tests.Migrations.070_courier_payout_legacy_integrity_preflight.sql",
            "Mono.Api.Tests.Migrations.071_courier_payout_legacy_integrity_check.sql",
            "Mono.Api.Tests.Migrations.072_courier_ledger_statement_index.sql",
            "Mono.Api.Tests.Migrations.073_courier_withdrawal_operations_search_filters.sql",
            "Mono.Api.Tests.Migrations.074_courier_withdrawal_operations_audit_read.sql",
            "Mono.Api.Tests.Migrations.075_courier_withdrawal_operations_event_search_audit.sql",
            "Mono.Api.Tests.Migrations.076_courier_payout_destination_operations_search_audit.sql",
            "Mono.Api.Tests.Migrations.077_courier_payout_reconciliation_read_audit.sql",
            "Mono.Api.Tests.Migrations.078_courier_payout_ledger_detail_read_audit.sql",
            "Mono.Api.Tests.Migrations.079_courier_payout_ledger_detail_search_audit.sql",
            "Mono.Api.Tests.Migrations.080_courier_payout_reconciliation_summary_audit.sql",
            "Mono.Api.Tests.Migrations.081_courier_payout_reconciliation_trend_audit.sql",
            "Mono.Api.Tests.Migrations.082_courier_payout_reconciliation_indexes.sql"
        };
        foreach (var resourceName in resources.Take(includePayoutIntegrity ? resources.Length : 5))
        {
            await using var stream = typeof(CourierWithdrawalTests).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("Missing embedded migration resource: " + resourceName);
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync();
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class TestOperationsContext : IMonoOperationsContext
    {
        public string ActorId => "ops_test_actor";
        public IReadOnlyCollection<string> Roles => [MonoOperationsRoles.Administrator];
    }
}
