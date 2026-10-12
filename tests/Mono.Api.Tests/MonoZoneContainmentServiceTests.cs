using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;
using NetTopologySuite.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

public sealed class MonoZoneContainmentServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Accepts_inside_and_border_touching_children_and_reports_partial_outside()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        var schema = "zone_containment_" + Guid.NewGuid().ToString("N");
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

            var parent = await PersistAsync(repository, "city", ZoneKind.City,
                "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");
            var inside = await PersistAsync(repository, "operating-inside", ZoneKind.OperatingZone,
                "POLYGON ((1 1, 3 1, 3 3, 1 3, 1 1))");
            var border = await PersistAsync(repository, "operating-border", ZoneKind.OperatingZone,
                "POLYGON ((0 4, 2 4, 2 6, 0 6, 0 4))");
            var outside = await PersistAsync(repository, "operating-outside", ZoneKind.OperatingZone,
                "POLYGON ((9 9, 12 9, 12 12, 9 12, 9 9))");

            var service = new MonoZoneGeometryQualityService(repository, new TestOperationsContext());
            var report = await service.CheckContainmentAsync(
                "City", parent, "OperatingZone", [inside, border, outside]);

            Assert.Equal("City", report.ParentKind);
            Assert.Equal("OperatingZone", report.ChildKind);
            Assert.Equal(3, report.ChildCount);
            Assert.False(report.Passed);
            var finding = Assert.Single(report.Findings);
            Assert.Equal("operating-outside", finding.Child.ZoneId);
            Assert.Equal("city", finding.Parent.ZoneId);
            Assert.Equal("OutsideParent", finding.Issue);

            var clean = await service.CheckContainmentAsync(
                "City", parent, "OperatingZone", [inside, border]);
            Assert.True(clean.Passed);
            Assert.Empty(clean.Findings);
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
    public async Task Rejects_unsupported_hierarchy_and_invalid_child_list_without_database_access()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            "Host=localhost;Database=unused;Username=unused;Password=unused");
        var service = new MonoZoneGeometryQualityService(
            new PostgresZoneGeometryRepository(dataSource), new TestOperationsContext());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CheckContainmentAsync(
            "City", new ZoneGeometrySnapshotReference("city", "v1"),
            "MicroZone", [new ZoneGeometrySnapshotReference("micro", "v1")]));
        await Assert.ThrowsAsync<ArgumentException>(() => service.CheckContainmentAsync(
            "City", new ZoneGeometrySnapshotReference("city", "v1"),
            "OperatingZone", []));
    }

    private static async Task<ZoneGeometrySnapshotReference> PersistAsync(
        PostgresZoneGeometryRepository repository, string zoneId, ZoneKind kind, string wkt)
    {
        var geometry = new WKTReader().Read(wkt);
        geometry.SRID = 4326;
        var boundary = ZoneGeometryBoundary.Create(
            zoneId, kind, "v1", "openstreetmap", "revision-1",
            "© OpenStreetMap contributors", geometry);
        await repository.PersistAsync(boundary);
        return new ZoneGeometrySnapshotReference(zoneId, "v1");
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
            await using var stream = typeof(MonoZoneContainmentServiceTests).Assembly
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
        public string ActorId => "containment-test";
        public IReadOnlyCollection<string> Roles => [MonoOperationsRoles.Administrator];
    }
}
