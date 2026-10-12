using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;
using NetTopologySuite.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class MonoZoneCoveragePreviewServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Resolves_only_active_snapshots_and_exposes_ambiguous_borders()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var cityId = "city-" + Guid.NewGuid().ToString("N");
        var zoneA = "zone-a-" + Guid.NewGuid().ToString("N");
        var zoneB = "zone-b-" + Guid.NewGuid().ToString("N");
        var microId = "micro-" + Guid.NewGuid().ToString("N");
        var snapshots = new PostgresZoneGeometryRepository(dataSource);
        await snapshots.PersistAsync(Boundary(cityId, ZoneKind.City, "v1",
            "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))"));
        await snapshots.PersistAsync(Boundary(zoneA, ZoneKind.OperatingZone, "v1",
            "POLYGON ((0 0, 5 0, 5 5, 0 5, 0 0))"));
        await snapshots.PersistAsync(Boundary(zoneA, ZoneKind.OperatingZone, "v2",
            "POLYGON ((0 0, 6 0, 6 5, 0 5, 0 0))"));
        await snapshots.PersistAsync(Boundary(zoneB, ZoneKind.OperatingZone, "v1",
            "POLYGON ((5 0, 10 0, 10 5, 5 5, 5 0))"));
        await snapshots.PersistAsync(Boundary(microId, ZoneKind.MicroZone, "v1",
            "POLYGON ((1 1, 3 1, 3 3, 1 3, 1 1))"));

        var context = new TestOperationsContext("zone-admin", MonoOperationsRoles.Administrator);
        var reviews = new PostgresZoneGeometryReviewService(dataSource, context);
        var activations = new PostgresZoneGeometryActivationService(dataSource, context);
        foreach (var (zoneId, version) in new[]
        {
            (cityId, "v1"), (zoneA, "v1"), (zoneB, "v1"), (microId, "v1")
        })
        {
            var hash = await ReadSnapshotHashAsync(dataSource, zoneId, version);
            await reviews.RecordAsync(zoneId, version, hash, "APPROVE", "Preview fixture approved.");
            await activations.ActivateAsync(zoneId, version, hash, "Preview fixture activated.");
        }

        // v2 exists but is not active and must not affect coverage results.
        var preview = new MonoZoneCoveragePreviewService(snapshots, context);
        var matched = await preview.PreviewAsync(2, 2);
        Assert.Equal(4, matched.ActiveBoundaryCount);
        AssertLayer(matched, ZoneKind.City, ZonePointResolutionStatus.Matched, cityId);
        AssertLayer(matched, ZoneKind.OperatingZone, ZonePointResolutionStatus.Matched, zoneA);
        AssertLayer(matched, ZoneKind.MicroZone, ZonePointResolutionStatus.Matched, microId);

        var inactiveVersionArea = await preview.PreviewAsync(2, 5.5);
        AssertLayer(inactiveVersionArea, ZoneKind.OperatingZone,
            ZonePointResolutionStatus.Matched, zoneB);

        var border = await preview.PreviewAsync(2.5, 5);
        AssertLayer(border, ZoneKind.OperatingZone, ZonePointResolutionStatus.Ambiguous, null);
        Assert.Equal(new[] { zoneA, zoneB }.Order(StringComparer.Ordinal),
            Layer(border, ZoneKind.OperatingZone).CandidateZoneIds.Order(StringComparer.Ordinal));

        var outside = await preview.PreviewAsync(20, 20);
        AssertLayer(outside, ZoneKind.City, ZonePointResolutionStatus.Outside, null);
        Assert.Equal(4, outside.ActiveBoundaryCount);
    }

    [Fact]
    public async Task Marks_unconfigured_zone_levels_and_rejects_invalid_coordinates()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var context = new TestOperationsContext("zone-admin", MonoOperationsRoles.Administrator);
        var previews = new MonoZoneCoveragePreviewService(
            new PostgresZoneGeometryRepository(dataSource), context);

        var empty = await previews.PreviewAsync(35.7, 51.4);
        Assert.False(empty.HasAnyActiveBoundaries);
        Assert.All(empty.Layers, layer =>
        {
            Assert.False(layer.HasActiveBoundaries);
            Assert.Null(layer.Status);
        });
        await Assert.ThrowsAsync<ArgumentException>(() => previews.PreviewAsync(91, 181));
    }

    private static ZoneGeometryBoundary Boundary(
        string zoneId, ZoneKind kind, string version, string wkt)
    {
        var geometry = new WKTReader().Read(wkt);
        geometry.SRID = 4326;
        return ZoneGeometryBoundary.Create(zoneId, kind, version,
            "openstreetmap", "source-revision-1", "© OpenStreetMap contributors", geometry);
    }

    private static async Task<string> ReadSnapshotHashAsync(
        NpgsqlDataSource dataSource, string zoneId, string version)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            SELECT snapshot_sha256 FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @version;
            """, connection);
        command.Parameters.AddWithValue("zone_id", zoneId);
        command.Parameters.AddWithValue("version", version);
        return (string)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Snapshot hash was not stored."));
    }

    private static ZoneCoverageLayerPreview Layer(
        ZoneCoveragePreviewResponse response, ZoneKind kind) =>
        Assert.Single(response.Layers.Where(layer => layer.Kind == kind));

    private static void AssertLayer(
        ZoneCoveragePreviewResponse response,
        ZoneKind kind,
        ZonePointResolutionStatus? expectedStatus,
        string? expectedZoneId)
    {
        var layer = Layer(response, kind);
        Assert.True(layer.HasActiveBoundaries);
        Assert.Equal(expectedStatus, layer.Status);
        Assert.Equal(expectedZoneId, layer.ZoneId);
    }

    private static async Task ApplyMigrationsAsync(NpgsqlDataSource dataSource)
    {
        foreach (var migration in new[]
        {
            "Mono.Api.Tests.Migrations.029_zone_geometry_versions.sql",
            "Mono.Api.Tests.Migrations.030_zone_geometry_reviews.sql",
            "Mono.Api.Tests.Migrations.031_zone_geometry_activation.sql"
        })
        {
            await using var stream = typeof(MonoZoneCoveragePreviewServiceTests).Assembly
                .GetManifestResourceStream(migration)
                ?? throw new InvalidOperationException($"Missing embedded migration resource: {migration}");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync();
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private sealed class TestOperationsContext(string actorId, params string[] roles)
        : IMonoOperationsContext
    {
        public string ActorId { get; } = actorId;
        public IReadOnlyCollection<string> Roles { get; } = roles;
    }
}
