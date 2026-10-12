using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;
using NetTopologySuite.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

public sealed class MonoZoneCoverageCompletenessServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Detects_uncovered_parent_area_and_children_extending_outside()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        var schema = "zone_coverage_" + Guid.NewGuid().ToString("N");
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
            var left = await PersistAsync(repository, "left", ZoneKind.OperatingZone,
                "POLYGON ((0 0, 5 0, 5 10, 0 10, 0 0))");
            var right = await PersistAsync(repository, "right", ZoneKind.OperatingZone,
                "POLYGON ((5 0, 10 0, 10 10, 5 10, 5 0))");
            var outside = await PersistAsync(repository, "outside", ZoneKind.OperatingZone,
                "POLYGON ((9 9, 12 9, 12 12, 9 12, 9 9))");

            var service = new MonoZoneCoverageCompletenessService(
                repository, new TestOperationsContext());

            var complete = await service.CheckAsync(parent, [left, right]);
            Assert.True(complete.Passed);
            Assert.True(complete.HasNoGaps);
            Assert.True(complete.AllChildrenWithinParent);
            Assert.Empty(complete.GapEnvelopes);
            Assert.Empty(complete.ChildrenOutsideParent);

            var gap = await service.CheckAsync(parent, [left]);
            Assert.False(gap.Passed);
            Assert.False(gap.HasNoGaps);
            var gapEnvelope = Assert.Single(gap.GapEnvelopes);
            Assert.Equal(5d, gapEnvelope.MinLongitude, 6);
            Assert.Equal(10d, gapEnvelope.MaxLongitude, 6);

            var extended = await service.CheckAsync(parent, [left, right, outside]);
            Assert.True(extended.HasNoGaps);
            Assert.False(extended.AllChildrenWithinParent);
            Assert.False(extended.Passed);
            Assert.Equal("outside", Assert.Single(extended.ChildrenOutsideParent).ZoneId);
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
    public async Task Rejects_missing_children_and_parent_kind_without_database_access()
    {
        await using var dataSource = NpgsqlDataSource.Create(
            "Host=localhost;Database=unused;Username=unused;Password=unused");
        var service = new MonoZoneCoverageCompletenessService(
            new PostgresZoneGeometryRepository(dataSource), new TestOperationsContext());

        await Assert.ThrowsAsync<ArgumentException>(() => service.CheckAsync(
            new ZoneGeometrySnapshotReference("micro", "v1"), []));
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
            await using var stream = typeof(MonoZoneCoverageCompletenessServiceTests).Assembly
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
        public string ActorId => "coverage-test";
        public IReadOnlyCollection<string> Roles => [MonoOperationsRoles.Administrator];
    }
}
