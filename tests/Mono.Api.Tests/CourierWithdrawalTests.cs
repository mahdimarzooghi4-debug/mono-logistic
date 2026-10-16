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

            var request = await withdrawals.CreateWithdrawalAsync(courierId, 30_000,
                "withdraw-001", now, CancellationToken.None);
            Assert.Equal("REQUESTED", request.OutcomeCode);
            Assert.NotNull(request.Withdrawal);
            Assert.Equal("IR********************9012", request.Withdrawal.MaskedIban);

            var ledger = new PostgresCourierEarningLedger(dataSource);
            var reservedBalance = await ledger.GetAvailableBalanceAsync(courierId, now, CancellationToken.None);
            Assert.Equal(14_500L, reservedBalance.AmountToman);

            var replay = await withdrawals.CreateWithdrawalAsync(courierId, 30_000,
                "withdraw-001", now.AddMinutes(1), CancellationToken.None);
            Assert.True(replay.AlreadyRequested);
            Assert.Equal(request.Withdrawal!.WithdrawalId, replay.Withdrawal!.WithdrawalId);
            var conflict = await withdrawals.CreateWithdrawalAsync(courierId, 29_000,
                "withdraw-001", now.AddMinutes(1), CancellationToken.None);
            Assert.Equal("IDEMPOTENCY_CONFLICT", conflict.OutcomeCode);

            var processing = await withdrawals.TransitionAsync(request.Withdrawal.WithdrawalId,
                "PROCESSING", null, null, "process-001", now.AddMinutes(2), CancellationToken.None);
            Assert.Equal("PROCESSING", processing.OutcomeCode);
            var paid = await withdrawals.TransitionAsync(request.Withdrawal.WithdrawalId,
                "PAID", null, "bank:trace-001", "pay-001", now.AddMinutes(3), CancellationToken.None);
            Assert.Equal("PAID", paid.OutcomeCode);
            var paidBalance = await ledger.GetAvailableBalanceAsync(courierId, now, CancellationToken.None);
            Assert.Equal(14_500L, paidBalance.AmountToman);

            var second = await withdrawals.CreateWithdrawalAsync(courierId, 10_000,
                "withdraw-002", now.AddMinutes(4), CancellationToken.None);
            Assert.Equal("REQUESTED", second.OutcomeCode);
            var releasedBalance = await ledger.GetAvailableBalanceAsync(courierId, now, CancellationToken.None);
            Assert.Equal(4_500L, releasedBalance.AmountToman);

            var rejected = await withdrawals.TransitionAsync(second.Withdrawal!.WithdrawalId,
                "REJECTED", "MANUAL_REVIEW_REJECTED", null, "reject-002",
                now.AddMinutes(5), CancellationToken.None);
            Assert.Equal("REJECTED", rejected.OutcomeCode);
            var restoredBalance = await ledger.GetAvailableBalanceAsync(courierId, now, CancellationToken.None);
            Assert.Equal(14_500L, restoredBalance.AmountToman);

            var third = await withdrawals.CreateWithdrawalAsync(courierId, 1_000,
                "withdraw-003", now.AddMinutes(6), CancellationToken.None);
            Assert.Equal("REQUESTED", third.OutcomeCode);
            var dailyLimit = await withdrawals.CreateWithdrawalAsync(courierId, 1_000,
                "withdraw-004", now.AddMinutes(7), CancellationToken.None);
            Assert.Equal("DAILY_LIMIT_REACHED", dailyLimit.OutcomeCode);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(connectionString);
            await admin.OpenAsync();
            await using var dropSchema = new NpgsqlCommand($"DROP SCHEMA {schemaName} CASCADE;", admin);
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    private static async Task ApplyMigrationAsync(NpgsqlDataSource dataSource)
    {
        foreach (var resourceName in new[]
        {
            "Mono.Api.Tests.Migrations.064_courier_earning_ledger.sql",
            "Mono.Api.Tests.Migrations.065_courier_manual_withdrawals.sql"
        })
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
