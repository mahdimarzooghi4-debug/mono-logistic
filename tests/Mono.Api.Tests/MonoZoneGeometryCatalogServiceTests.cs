using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;
using NetTopologySuite.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

public sealed class MonoZoneGeometryCatalogServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Lists_verified_snapshots_with_latest_review_and_keyset_pagination()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        var schema = "zone_catalog_" + Guid.NewGuid().ToString("N");
        var adminBuilder = new NpgsqlConnectionStringBuilder(connectionString);
        await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var createSchema = new NpgsqlCommand(
                $"CREATE SCHEMA {schema};", admin);
            await createSchema.ExecuteNonQueryAsync();
        }

        try
        {
            var scopedBuilder = new NpgsqlConnectionStringBuilder(connectionString) { SearchPath = schema };
            await using var dataSource = NpgsqlDataSource.Create(scopedBuilder.ConnectionString);
            await ApplyMigrationAsync(dataSource, "029_zone_geometry_versions.sql");
            await ApplyMigrationAsync(dataSource, "030_zone_geometry_reviews.sql");
            await ApplyMigrationAsync(dataSource, "031_zone_geometry_activation.sql");

            var repository = new PostgresZoneGeometryRepository(dataSource);
            var zoneId = "catalog-" + Guid.NewGuid().ToString("N");
            var v1 = Boundary(zoneId, "v1", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");
            var v2 = Boundary(zoneId, "v2", "POLYGON ((0 0, 20 0, 20 20, 0 20, 0 0))");
            await repository.PersistAsync(v1);
            await repository.PersistAsync(v2);
            await AddReviewAsync(dataSource, zoneId, "v1", "APPROVE");
            await AddReviewAsync(dataSource, zoneId, "v2", "APPROVE");
            await AddReviewAsync(dataSource, zoneId, "v2", "REJECT");
            await ActivateAsync(dataSource, zoneId, "v1");

            var context = new TestOperationsContext();
            var service = new MonoZoneGeometryCatalogService(
                dataSource, repository, context);
            var first = await service.ListAsync("OperatingZone", 1, null);
            var second = await service.ListAsync("OperatingZone", 1, first.NextCursor);

            Assert.Single(first.Items);
            Assert.True(first.HasMore);
            Assert.NotNull(first.NextCursor);
            Assert.Equal("v1", first.Items[0].GeometryVersion);
            Assert.True(first.Items[0].IsActive);
            Assert.True(first.Items[0].IntegrityVerified);
            Assert.NotNull(first.Items[0].LatestReview);
            Assert.Equal("APPROVE", first.Items[0].LatestReview.Decision);

            Assert.Single(second.Items);
            Assert.False(second.HasMore);
            Assert.Null(second.NextCursor);
            Assert.Equal("v2", second.Items[0].GeometryVersion);
            Assert.False(second.Items[0].IsActive);
            Assert.True(second.Items[0].IntegrityVerified);
            Assert.Equal("REJECT", second.Items[0].LatestReview!.Decision);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
            await admin.OpenAsync();
            await using var dropSchema = new NpgsqlCommand(
                $"DROP SCHEMA {NpgsqlCommandBuilder.QuoteIdentifier(schema)} CASCADE;", admin);
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Rejects_invalid_filter_page_size_and_cursor_before_database_access()
    {
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Database=unused;Username=unused;Password=unused");
        try
        {
            var service = new MonoZoneGeometryCatalogService(
                dataSource, new PostgresZoneGeometryRepository(dataSource), new TestOperationsContext());
            await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync("Unknown", null, null));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ListAsync(null, 101, null));
            await Assert.ThrowsAsync<ArgumentException>(() => service.ListAsync(null, null, "bad-cursor"));
        }
        finally
        {
            await dataSource.DisposeAsync();
        }
    }

    private static ZoneGeometryBoundary Boundary(string zoneId, string version, string wkt)
    {
        var geometry = new WKTReader().Read(wkt);
        geometry.SRID = 4326;
        return ZoneGeometryBoundary.Create(
            zoneId, ZoneKind.OperatingZone, version, "openstreetmap",
            "revision-1", "© OpenStreetMap contributors", geometry);
    }

    private static async Task AddReviewAsync(
        NpgsqlDataSource dataSource, string zoneId, string version, string decision)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO zone_geometry_reviews (
                zone_id, geometry_version, snapshot_sha256, decision, actor_id, reason, occurred_at)
            SELECT @zone_id, @version, snapshot_sha256, @decision, 'catalog-test', 'catalog test',
                   now()
            FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @version;
            """, connection);
        command.Parameters.AddWithValue("zone_id", zoneId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("decision", decision);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task ActivateAsync(
        NpgsqlDataSource dataSource, string zoneId, string version)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO zone_geometry_active (
                zone_id, geometry_version, snapshot_sha256, activated_at, activated_by)
            SELECT @zone_id, @version, snapshot_sha256, now(), 'catalog-test'
            FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @version;
            """, connection);
        command.Parameters.AddWithValue("zone_id", zoneId);
        command.Parameters.AddWithValue("version", version);
        _ = snapshotHash;
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private static async Task ApplyMigrationAsync(NpgsqlDataSource dataSource, string migrationName)
    {
        var resourceName = "Mono.Api.Tests.Migrations." + migrationName;
        await using var stream = typeof(MonoZoneGeometryCatalogServiceTests).Assembly
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
        public string ActorId => "catalog-test";
        public IReadOnlyCollection<string> Roles => [MonoOperationsRoles.Administrator];
    }
}
