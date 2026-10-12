using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;
using NetTopologySuite.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

public sealed class MonoActiveZoneGeoJsonLayerServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Returns_only_active_features_with_keyset_pagination_and_hash_checks()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        var schema = "active_geojson_" + Guid.NewGuid().ToString("N");
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
            await ApplyMigrationsAsync(dataSource);
            var repository = new PostgresZoneGeometryRepository(dataSource);
            var activeA = await PersistAsync(repository, "active-a", "POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))");
            var activeB = await PersistAsync(repository, "active-b", "POLYGON ((4 4, 6 4, 6 6, 4 6, 4 4))");
            _ = await PersistAsync(repository, "inactive-c", "POLYGON ((8 8, 10 8, 10 10, 8 10, 8 8))");
            await ApproveAndActivateAsync(dataSource, activeA);
            await ApproveAndActivateAsync(dataSource, activeB);

            var geoJsonService = new MonoZoneGeometryGeoJsonService(
                dataSource, repository, new TestOperationsContext());
            var service = new MonoActiveZoneGeoJsonLayerService(
                dataSource, geoJsonService, new TestOperationsContext());

            var first = await service.GetAsync("OperatingZone", 1, null);
            var second = await service.GetAsync("OperatingZone", 1, first.NextCursor);

            Assert.Equal("FeatureCollection", first.GeoJson.Type);
            Assert.Equal(1, first.FeatureCount);
            Assert.Equal(5, first.CoordinateCount);
            Assert.True(first.HasMore);
            Assert.NotNull(first.NextCursor);
            Assert.Equal("active-a", first.GeoJson.Features[0].Properties.ZoneId);
            Assert.True(first.GeoJson.Features[0].Properties.CoordinateCount > 0);

            Assert.Equal(1, second.FeatureCount);
            Assert.False(second.HasMore);
            Assert.Null(second.NextCursor);
            Assert.Equal("active-b", second.GeoJson.Features[0].Properties.ZoneId);
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
    public async Task Rejects_invalid_kind_page_size_and_cursor_without_database_access()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            "Host=localhost;Database=unused;Username=unused;Password=unused");
        var repository = new PostgresZoneGeometryRepository(dataSource);
        var geoJsonService = new MonoZoneGeometryGeoJsonService(
            dataSource, repository, new TestOperationsContext());
        var service = new MonoActiveZoneGeoJsonLayerService(
            dataSource, geoJsonService, new TestOperationsContext());

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync("unknown", null, null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => service.GetAsync("City", 26, null));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.GetAsync("City", null, "invalid"));
    }

    private static async Task<ZoneGeometrySnapshotReference> PersistAsync(
        PostgresZoneGeometryRepository repository, string zoneId, string wkt)
    {
        var geometry = new WKTReader().Read(wkt);
        geometry.SRID = 4326;
        var boundary = ZoneGeometryBoundary.Create(
            zoneId, ZoneKind.OperatingZone, "v1", "openstreetmap",
            "revision-1", "© OpenStreetMap contributors", geometry);
        await repository.PersistAsync(boundary);
        return new ZoneGeometrySnapshotReference(zoneId, "v1");
    }

    private static async Task ApproveAndActivateAsync(
        NpgsqlDataSource dataSource, ZoneGeometrySnapshotReference snapshot)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using (var review = new NpgsqlCommand("""
            INSERT INTO zone_geometry_reviews (
                zone_id, geometry_version, snapshot_sha256, decision, actor_id, reason, occurred_at)
            SELECT @zone_id, @version, snapshot_sha256, 'APPROVE', 'active-layer-test',
                   'fixture', now()
            FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @version;
            """, connection))
        {
            review.Parameters.AddWithValue("zone_id", snapshot.ZoneId);
            review.Parameters.AddWithValue("version", snapshot.GeometryVersion);
            Assert.Equal(1, await review.ExecuteNonQueryAsync());
        }

        await using var activate = new NpgsqlCommand("""
            INSERT INTO zone_geometry_active (
                zone_id, geometry_version, snapshot_sha256, activated_at, activated_by)
            SELECT @zone_id, @version, snapshot_sha256, now(), 'active-layer-test'
            FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @version;
            """, connection);
        activate.Parameters.AddWithValue("zone_id", snapshot.ZoneId);
        activate.Parameters.AddWithValue("version", snapshot.GeometryVersion);
        Assert.Equal(1, await activate.ExecuteNonQueryAsync());
    }

    private static async Task ApplyMigrationsAsync(NpgsqlDataSource dataSource)
    {
        foreach (var migrationName in new[]
                 {
                     "029_zone_geometry_versions.sql",
                     "030_zone_geometry_reviews.sql",
                     "031_zone_geometry_activation.sql"
                 })
        {
            var resourceName = "Mono.Api.Tests.Migrations." + migrationName;
            await using var stream = typeof(MonoActiveZoneGeoJsonLayerServiceTests).Assembly
                .GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Missing embedded migration resource {resourceName}.");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync();
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class TestOperationsContext : IMonoOperationsContext
    {
        public string ActorId => "active-layer-test";
        public IReadOnlyCollection<string> Roles => [MonoOperationsRoles.Administrator];
    }
}
