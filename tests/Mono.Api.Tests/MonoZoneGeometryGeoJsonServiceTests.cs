using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;
using NetTopologySuite.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

public sealed class MonoZoneGeometryGeoJsonServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Returns_polygon_holes_and_multipolygons_in_geojson_coordinate_order()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        var schema = "zone_geojson_" + Guid.NewGuid().ToString("N");
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
            await ApplyMigrationAsync(dataSource);
            var repository = new PostgresZoneGeometryRepository(dataSource);
            var polygon = await PersistAsync(repository, "polygon", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0),(2 2, 2 4, 4 4, 4 2, 2 2))");
            var multi = await PersistAsync(repository, "multi",
                "MULTIPOLYGON (((20 20, 25 20, 25 25, 20 25, 20 20)), ((30 30, 35 30, 35 35, 30 35, 30 30)))");

            var service = new MonoZoneGeometryGeoJsonService(
                dataSource, repository, new TestOperationsContext());

            var polygonFeature = await service.GetAsync(polygon.ZoneId, polygon.GeometryVersion);
            Assert.Equal("Feature", polygonFeature.Type);
            Assert.Equal("Polygon", polygonFeature.Geometry.Type);
            Assert.Equal("polygon", polygonFeature.Properties.ZoneId);
            Assert.Equal("OperatingZone", polygonFeature.Properties.Kind);
            Assert.Equal(4326, polygonFeature.Properties.Srid);
            var rings = Assert.IsType<double[][][]>(polygonFeature.Geometry.Coordinates);
            Assert.Equal(2, rings.Length);
            Assert.Equal(new[] { 0d, 0d }, rings[0][0]);
            Assert.Equal(new[] { 2d, 2d }, rings[1][0]);

            var multiFeature = await service.GetAsync(multi.ZoneId, multi.GeometryVersion);
            Assert.Equal("MultiPolygon", multiFeature.Geometry.Type);
            var polygons = Assert.IsType<double[][][][]>(multiFeature.Geometry.Coordinates);
            Assert.Equal(2, polygons.Length);
            Assert.Equal(20d, polygons[0][0][0][0]);
            Assert.Equal(20d, polygons[0][0][0][1]);
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
    public async Task Rejects_blank_snapshot_references_without_database_access()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            "Host=localhost;Database=unused;Username=unused;Password=unused");
        var service = new MonoZoneGeometryGeoJsonService(
            dataSource, new PostgresZoneGeometryRepository(dataSource), new TestOperationsContext());

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(" ", "v1"));
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

    private static async Task ApplyMigrationAsync(NpgsqlDataSource dataSource)
    {
        foreach (var migrationName in new[]
                 {
                     "029_zone_geometry_versions.sql",
                     "030_zone_geometry_reviews.sql",
                     "031_zone_geometry_activation.sql"
                 })
        {
            var resourceName = "Mono.Api.Tests.Migrations." + migrationName;
            await using var stream = typeof(MonoZoneGeometryGeoJsonServiceTests).Assembly
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
        public string ActorId => "geojson-test";
        public IReadOnlyCollection<string> Roles => [MonoOperationsRoles.Administrator];
    }
}
