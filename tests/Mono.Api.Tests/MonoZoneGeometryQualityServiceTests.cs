using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;
using NetTopologySuite.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

public sealed class MonoZoneGeometryQualityServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Reports_interior_overlap_but_allows_shared_boundary_and_separate_polygons()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        var schema = "zone_quality_" + Guid.NewGuid().ToString("N");
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

            var first = await PersistAsync(repository, "zone-a", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");
            var overlap = await PersistAsync(repository, "zone-b", "POLYGON ((5 5, 9 5, 9 9, 5 9, 5 5))");
            var adjacent = await PersistAsync(repository, "zone-c", "POLYGON ((10 0, 20 0, 20 10, 10 10, 10 0))");
            var separate = await PersistAsync(repository, "zone-d", "POLYGON ((30 30, 40 30, 40 40, 30 40, 30 30))");

            var service = new MonoZoneGeometryQualityService(
                repository, new TestOperationsContext());
            var report = await service.CheckAsync("OperatingZone",
                [first, overlap, adjacent, separate]);

            Assert.Equal("OperatingZone", report.Kind);
            Assert.Equal(4, report.SnapshotCount);
            Assert.True(report.CoordinateCount > 0);
            Assert.False(report.Passed);
            var finding = Assert.Single(report.Findings);
            Assert.Equal("InteriorOverlap", finding.Issue);
            Assert.Equal("zone-a", finding.First.ZoneId);
            Assert.Equal("zone-b", finding.Second.ZoneId);

            var cleanReport = await service.CheckAsync("OperatingZone", [first, adjacent, separate]);
            Assert.True(cleanReport.Passed);
            Assert.Empty(cleanReport.Findings);
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
    public async Task Rejects_invalid_kind_snapshot_count_and_duplicate_zone_ids()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            "Host=localhost;Database=unused;Username=unused;Password=unused");
        var service = new MonoZoneGeometryQualityService(
            new PostgresZoneGeometryRepository(dataSource), new TestOperationsContext());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CheckAsync("unknown", []));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CheckAsync("City", []));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.CheckAsync("City",
                [new ZoneGeometrySnapshotReference("zone", "v1"),
                 new ZoneGeometrySnapshotReference("zone", "v2")]));
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
            await using var stream = typeof(MonoZoneGeometryQualityServiceTests).Assembly
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
        public string ActorId => "quality-test";
        public IReadOnlyCollection<string> Roles => [MonoOperationsRoles.Administrator];
    }
}
