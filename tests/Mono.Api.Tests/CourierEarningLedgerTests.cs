using Mono.Api.Persistence;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Mono.Api.Tests;

public sealed class CourierEarningLedgerTests
{
    [Fact]
    public async Task Delivered_mission_posts_one_balanced_idempotent_earning_and_balance_is_derived()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var schemaName = "courier_ledger_" + Guid.NewGuid().ToString("N");
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
                """, connection))
                await setup.ExecuteNonQueryAsync();
            await ApplyMigrationAsync(dataSource);

            const string missionId = "mis_11111111111111111111111111111111";
            const string customerId = "cus_11111111111111111111111111111111";
            const string courierId = "crr_11111111111111111111111111111111";
            var occurredAt = new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
            await using (var connection = await dataSource.OpenConnectionAsync())
            {
                await using var seedMission = new NpgsqlCommand("""
                    INSERT INTO missions VALUES (@mission_id, @customer_id, 'drq_1', 'quo_1', 1, 'DELIVERED');
                    """, connection);
                seedMission.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
                seedMission.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
                await seedMission.ExecuteNonQueryAsync();
                await using var seedQuote = new NpgsqlCommand("""
                    INSERT INTO delivery_quote_versions VALUES
                        (@customer_id, 'drq_1', 'quo_1', 1, '{"fare":{"fare_toman":50000}}'::jsonb);
                    """, connection);
                seedQuote.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
                await seedQuote.ExecuteNonQueryAsync();
                await using var seedOffer = new NpgsqlCommand("""
                    INSERT INTO mission_courier_offer_events (mission_id, customer_id, courier_id, action)
                    VALUES (@mission_id, @customer_id, @courier_id, 'COMPLETED');
                    """, connection);
                seedOffer.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
                seedOffer.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
                seedOffer.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
                await seedOffer.ExecuteNonQueryAsync();
            }

            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                Assert.True(await PostgresCourierEarningLedger.PostMissionEarningAsync(connection,
                    transaction, customerId, missionId, occurredAt, CancellationToken.None));
                await transaction.CommitAsync();
            }
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                Assert.False(await PostgresCourierEarningLedger.PostMissionEarningAsync(connection,
                    transaction, customerId, missionId, occurredAt.AddMinutes(1), CancellationToken.None));
                await transaction.CommitAsync();
            }

            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var command = new NpgsqlCommand("""
                SELECT ledger_tx.reference_type, COUNT(entry.ledger_entry_id),
                       SUM(entry.debit_amount)::bigint, SUM(entry.credit_amount)::bigint
                FROM ledger_transactions ledger_tx
                JOIN ledger_entries entry USING (ledger_transaction_id)
                WHERE ledger_tx.reference_id = @mission_id
                GROUP BY ledger_tx.ledger_transaction_id, ledger_tx.reference_type;
                """, connection))
            {
                command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
                await using var reader = await command.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal("MISSION_EARNING", reader.GetString(0));
                Assert.Equal(4L, reader.GetInt64(1));
                Assert.Equal(50_000L, reader.GetInt64(2));
                Assert.Equal(50_000L, reader.GetInt64(3));
                Assert.False(await reader.ReadAsync());
            }

            var ledger = new PostgresCourierEarningLedger(dataSource);
            var balance = await ledger.GetAvailableBalanceAsync(courierId, occurredAt, CancellationToken.None);
            Assert.Equal(44_500L, balance.AmountToman);
            Assert.Equal(occurredAt, balance.ObservedAt);

            const string secondMissionId = "mis_22222222222222222222222222222222";
            var secondOccurredAt = occurredAt.AddMinutes(2);
            await using (var connection = await dataSource.OpenConnectionAsync())
            {
                await using var seedMission = new NpgsqlCommand("""
                    INSERT INTO missions VALUES (@mission_id, @customer_id, 'drq_2', 'quo_2', 1, 'DELIVERED');
                    """, connection);
                seedMission.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, secondMissionId);
                seedMission.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
                await seedMission.ExecuteNonQueryAsync();
                await using var seedQuote = new NpgsqlCommand("""
                    INSERT INTO delivery_quote_versions VALUES
                        (@customer_id, 'drq_2', 'quo_2', 1, '{"fare":{"fare_toman":75000}}'::jsonb);
                    """, connection);
                seedQuote.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
                await seedQuote.ExecuteNonQueryAsync();
                await using var seedOffer = new NpgsqlCommand("""
                    INSERT INTO mission_courier_offer_events (mission_id, customer_id, courier_id, action)
                    VALUES (@mission_id, @customer_id, @courier_id, 'COMPLETED');
                    """, connection);
                seedOffer.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, secondMissionId);
                seedOffer.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
                seedOffer.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
                await seedOffer.ExecuteNonQueryAsync();
            }
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var transaction = await connection.BeginTransactionAsync())
            {
                Assert.True(await PostgresCourierEarningLedger.PostMissionEarningAsync(connection,
                    transaction, customerId, secondMissionId, secondOccurredAt, CancellationToken.None));
                await transaction.CommitAsync();
            }

            var firstPage = await ledger.GetEarningHistoryAsync(courierId, 1, null, CancellationToken.None);
            Assert.Single(firstPage.Items);
            Assert.Equal(secondMissionId, firstPage.Items[0].MissionId);
            Assert.Equal(66_750L, firstPage.Items[0].AmountToman);
            Assert.NotNull(firstPage.NextBeforeEntryId);
            var secondPage = await ledger.GetEarningHistoryAsync(courierId, 1,
                firstPage.NextBeforeEntryId, CancellationToken.None);
            Assert.Single(secondPage.Items);
            Assert.Equal(missionId, secondPage.Items[0].MissionId);
            Assert.Null(secondPage.NextBeforeEntryId);

            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var mutation = new NpgsqlCommand("UPDATE ledger_entries SET credit_amount = 1;", connection))
                await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync());

            await ApplyMigrationAsync(dataSource);
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
        const string resourceName = "Mono.Api.Tests.Migrations.064_courier_earning_ledger.sql";
        await using var stream = typeof(CourierEarningLedgerTests).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Missing embedded migration resource: " + resourceName);
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
