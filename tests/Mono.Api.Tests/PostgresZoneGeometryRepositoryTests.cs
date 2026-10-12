using Mono.Api.Domain;
using Mono.Api.Persistence;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class PostgresZoneGeometryRepositoryTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Stores_replays_reads_and_keeps_zone_versions_append_only()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationAsync(dataSource);
        var repository = new PostgresZoneGeometryRepository(dataSource);
        var zoneId = "zone-" + Guid.NewGuid().ToString("N");
        var first = Boundary(zoneId, "v1", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");

        var concurrent = await Task.WhenAll(
            repository.PersistAsync(first),
            repository.PersistAsync(first));
        var read = await repository.FindAsync(zoneId, "v1");
        var changedSameVersion = await repository.PersistAsync(
            Boundary(zoneId, "v1", "POLYGON ((0 0, 20 0, 20 20, 0 20, 0 0))"));
        var nextVersion = await repository.PersistAsync(
            Boundary(zoneId, "v2", "POLYGON ((0 0, 20 0, 20 20, 0 20, 0 0))"));

        Assert.Single(concurrent, result => result.Outcome == ZoneGeometryWriteOutcome.Persisted);
        Assert.Single(concurrent, result => result.Outcome == ZoneGeometryWriteOutcome.Replayed);
        Assert.NotNull(read);
        Assert.Equal("v1", read.GeometryVersion);
        Assert.Equal(ZonePointRelation.Inside, read.Classify(new GeoCoordinate(5, 5)));
        Assert.Equal(ZoneGeometryWriteOutcome.Conflict, changedSameVersion.Outcome);
        Assert.Equal(ZoneGeometryWriteOutcome.Persisted, nextVersion.Outcome);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*), min(geometry_version), max(geometry_version)
            FROM zone_geometry_versions
            WHERE zone_id = @zone_id;
            """, connection);
        count.Parameters.AddWithValue("zone_id", zoneId);
        await using (var reader = await count.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal(2L, reader.GetInt64(0));
            Assert.Equal("v1", reader.GetString(1));
            Assert.Equal("v2", reader.GetString(2));
        }

        await using var update = new NpgsqlCommand("""
            UPDATE zone_geometry_versions SET attribution = 'tampered'
            WHERE zone_id = @zone_id AND geometry_version = 'v1';
            """, connection);
        update.Parameters.AddWithValue("zone_id", zoneId);
        await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());

        await using var delete = new NpgsqlCommand("""
            DELETE FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = 'v1';
            """, connection);
        delete.Parameters.AddWithValue("zone_id", zoneId);
        await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());

        await using var truncate = new NpgsqlCommand("TRUNCATE zone_geometry_versions;", connection);
        await Assert.ThrowsAsync<PostgresException>(() => truncate.ExecuteNonQueryAsync());

        var intact = await repository.FindAsync(zoneId, "v1");
        Assert.NotNull(intact);
        Assert.Equal(ZonePointRelation.Inside, intact.Classify(new GeoCoordinate(5, 5)));
    }

    [Fact]
    public async Task Returns_null_for_unknown_zone_geometry_version()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationAsync(dataSource);
        var repository = new PostgresZoneGeometryRepository(dataSource);

        Assert.Null(await repository.FindAsync("missing-" + Guid.NewGuid().ToString("N"), "v1"));
    }

    private static ZoneGeometryBoundary Boundary(string zoneId, string version, string wkt)
    {
        var geometry = new WKTReader().Read(wkt);
        geometry.SRID = 4326;
        return ZoneGeometryBoundary.Create(
            zoneId,
            ZoneKind.OperatingZone,
            version,
            "openstreetmap",
            "dataset-revision-2026-10",
            "© OpenStreetMap contributors",
            geometry);
    }

    private static async Task ApplyMigrationAsync(NpgsqlDataSource dataSource)
    {
        var assembly = typeof(PostgresZoneGeometryRepositoryTests).Assembly;
        await using var stream = assembly.GetManifestResourceStream(
            "Mono.Api.Tests.Migrations.029_zone_geometry_versions.sql")
            ?? throw new InvalidOperationException("Missing embedded zone geometry migration resource.");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
