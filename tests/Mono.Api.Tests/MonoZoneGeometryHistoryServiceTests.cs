using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;
using NetTopologySuite.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

public sealed class MonoZoneGeometryHistoryServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Returns_review_and_activation_events_in_one_paginated_timeline()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        var schema = "zone_history_" + Guid.NewGuid().ToString("N");
        var adminConnectionString = new NpgsqlConnectionStringBuilder(connectionString).ConnectionString;
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema};", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var scoped = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            await using var dataSource = NpgsqlDataSource.Create(scoped.ConnectionString);
            await ApplyMigrationAsync(dataSource, "029_zone_geometry_versions.sql");
            await ApplyMigrationAsync(dataSource, "030_zone_geometry_reviews.sql");
            await ApplyMigrationAsync(dataSource, "031_zone_geometry_activation.sql");

            var repository = new PostgresZoneGeometryRepository(dataSource);
            var zoneId = "history-" + Guid.NewGuid().ToString("N");
            var version = "v1";
            var geometry = new WKTReader().Read("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");
            geometry.SRID = 4326;
            var boundary = ZoneGeometryBoundary.Create(
                zoneId, ZoneKind.OperatingZone, version, "openstreetmap", "revision-1",
                "© OpenStreetMap contributors", geometry);
            await repository.PersistAsync(boundary);

            var start = DateTimeOffset.UtcNow;
            await InsertReviewAsync(dataSource, zoneId, version, "APPROVE", start);
            await InsertActivationEventAsync(dataSource, zoneId, version, "ACTIVATE", start.AddSeconds(1));
            await InsertActivePointerAsync(dataSource, zoneId, version, start.AddSeconds(1));
            await DeleteActivePointerAsync(dataSource, zoneId);
            await InsertActivationEventAsync(dataSource, zoneId, version, "DEACTIVATE", start.AddSeconds(2));

            var service = new MonoZoneGeometryHistoryService(
                dataSource, repository, new TestOperationsContext());
            var first = await service.GetAsync(zoneId, version, 2, null);
            var second = await service.GetAsync(zoneId, version, 2, first.NextCursor);

            Assert.Equal("v1", first.Snapshot.GeometryVersion);
            Assert.Equal(boundary.ZoneId, first.Snapshot.ZoneId);
            Assert.Equal(2, first.Events.Count);
            Assert.True(first.HasMore);
            Assert.NotNull(first.NextCursor);
            Assert.Equal(new[] { "REVIEW:APPROVE", "ACTIVATION:ACTIVATE" },
                first.Events.Select(item => $"{item.EventType}:{item.Action}"));
            Assert.All(first.Events, item => Assert.Equal(first.Snapshot.SnapshotHash, item.SnapshotHash));

            Assert.Single(second.Events);
            Assert.False(second.HasMore);
            Assert.Null(second.NextCursor);
            Assert.Equal("ACTIVATION", second.Events[0].EventType);
            Assert.Equal("DEACTIVATE", second.Events[0].Action);
            Assert.Equal(first.Snapshot.SnapshotHash, second.Events[0].SnapshotHash);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(adminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE;", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Rejects_invalid_page_size_and_cursor_without_opening_database()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            "Host=localhost;Database=unused;Username=unused;Password=unused");
        var service = new MonoZoneGeometryHistoryService(
            dataSource, new PostgresZoneGeometryRepository(dataSource), new TestOperationsContext());

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetAsync("zone", "v1", 101, null));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetAsync("zone", "v1", null, "invalid"));
    }

    private static async Task InsertReviewAsync(
        NpgsqlDataSource dataSource, string zoneId, string version, string decision, DateTimeOffset occurredAt)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO zone_geometry_reviews (
                zone_id, geometry_version, snapshot_sha256, decision, actor_id, reason, occurred_at)
            SELECT @zone_id, @version, snapshot_sha256, @decision, 'history-test', 'history test', @occurred_at
            FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @version;
            """, connection);
        command.Parameters.AddWithValue("zone_id", zoneId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("decision", decision);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertActivationEventAsync(
        NpgsqlDataSource dataSource, string zoneId, string version, string action, DateTimeOffset occurredAt)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO zone_geometry_activation_events (
                zone_id, geometry_version, snapshot_sha256, action, actor_id, reason, occurred_at)
            SELECT @zone_id, @version, snapshot_sha256, @action, 'history-test', 'history test', @occurred_at
            FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @version;
            """, connection);
        command.Parameters.AddWithValue("zone_id", zoneId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("occurred_at", occurredAt);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task InsertActivePointerAsync(
        NpgsqlDataSource dataSource, string zoneId, string version, DateTimeOffset activatedAt)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO zone_geometry_active (
                zone_id, geometry_version, snapshot_sha256, activated_at, activated_by)
            SELECT @zone_id, @version, snapshot_sha256, @activated_at, 'history-test'
            FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @version;
            """, connection);
        command.Parameters.AddWithValue("zone_id", zoneId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("activated_at", activatedAt);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task DeleteActivePointerAsync(NpgsqlDataSource dataSource, string zoneId)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(
            "DELETE FROM zone_geometry_active WHERE zone_id = @zone_id;", connection);
        command.Parameters.AddWithValue("zone_id", zoneId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task ApplyMigrationAsync(NpgsqlDataSource dataSource, string migrationName)
    {
        var resourceName = "Mono.Api.Tests.Migrations." + migrationName;
        await using var stream = typeof(MonoZoneGeometryHistoryServiceTests).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded migration resource {resourceName}.");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestOperationsContext : IMonoOperationsContext
    {
        public string ActorId => "history-test";
        public IReadOnlyCollection<string> Roles => [MonoOperationsRoles.Administrator];
    }
}
