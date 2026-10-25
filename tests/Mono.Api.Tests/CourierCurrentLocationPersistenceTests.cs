using Mono.Api.Domain;
using Mono.Api.Persistence;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

public sealed class CourierCurrentLocationPersistenceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Courier_can_clear_only_its_own_current_position()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        var schema = "courier_location_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connectionString);
        await admin.OpenAsync();
        await using (var createSchema = new NpgsqlCommand($"CREATE SCHEMA {schema};", admin))
            await createSchema.ExecuteNonQueryAsync();

        var scopedConnectionString = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema }.ConnectionString;
        await using var dataSource = NpgsqlDataSource.Create(scopedConnectionString);
        try
        {
            const string setup = """
                CREATE TABLE couriers (courier_id text PRIMARY KEY);
                CREATE TABLE courier_current_locations (
                    courier_id text PRIMARY KEY REFERENCES couriers(courier_id) ON DELETE CASCADE,
                    latitude double precision NOT NULL,
                    longitude double precision NOT NULL,
                    accuracy_meters double precision NULL,
                    heading_degrees double precision NULL,
                    speed_meters_per_second double precision NULL,
                    observed_at timestamptz NOT NULL,
                    active_route_id text NULL,
                    updated_at timestamptz NOT NULL);
                INSERT INTO couriers (courier_id) VALUES ('crr_location_one'), ('crr_location_two');
                """;
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var command = new NpgsqlCommand(setup, connection))
                await command.ExecuteNonQueryAsync();

            var service = new PostgresCourierCurrentLocationService(dataSource, TimeProvider.System);
            var observedAt = DateTimeOffset.UtcNow;
            foreach (var courierId in new[] { "crr_location_one", "crr_location_two" })
            {
                await service.UpdateAsync(courierId, new CourierLocationUpdate
                {
                    Latitude = 35.7,
                    Longitude = 51.4,
                    ObservedAt = observedAt
                });
            }

            await service.ClearAsync("crr_location_one");
            await service.ClearAsync("crr_location_one");

            await using var verify = await dataSource.OpenConnectionAsync();
            await using var read = new NpgsqlCommand(
                "SELECT courier_id FROM courier_current_locations ORDER BY courier_id;", verify);
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("crr_location_two", reader.GetString(0));
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            await dataSource.DisposeAsync();
            await using var dropSchema = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE;", admin);
            await dropSchema.ExecuteNonQueryAsync();
        }
    }
}
