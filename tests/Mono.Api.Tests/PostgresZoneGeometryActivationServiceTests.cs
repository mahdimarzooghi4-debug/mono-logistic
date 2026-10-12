using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using NetTopologySuite.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class PostgresZoneGeometryActivationServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Requires_latest_approval_switches_versions_and_audits_deactivation()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var zoneId = "activation-" + Guid.NewGuid().ToString("N");
        var snapshots = new PostgresZoneGeometryRepository(dataSource);
        await snapshots.PersistAsync(Boundary(zoneId, "v1", 2));
        await snapshots.PersistAsync(Boundary(zoneId, "v2", 3));
        var hashV1 = await ReadSnapshotHashAsync(dataSource, zoneId, "v1");
        var hashV2 = await ReadSnapshotHashAsync(dataSource, zoneId, "v2");

        var context = new TestOperationsContext("zone-admin", MonoOperationsRoles.Administrator);
        var activations = new PostgresZoneGeometryActivationService(dataSource, context);
        await Assert.ThrowsAsync<ZoneGeometryActivationReviewRequiredException>(() =>
            activations.ActivateAsync(zoneId, "v1", hashV1, "Initial activation."));

        var reviews = new PostgresZoneGeometryReviewService(dataSource, context);
        await reviews.RecordAsync(zoneId, "v1", hashV1, "APPROVE", "Checked v1 boundary.");
        var activeV1 = await activations.ActivateAsync(zoneId, "v1", hashV1, "Initial activation.");
        Assert.Equal("v1", activeV1.GeometryVersion);

        await reviews.RecordAsync(zoneId, "v2", hashV2, "APPROVE", "Checked updated boundary.");
        var activeV2 = await activations.ActivateAsync(zoneId, "v2", hashV2, "Replace with updated boundary.");
        Assert.Equal("v2", activeV2.GeometryVersion);
        await Assert.ThrowsAsync<ZoneGeometrySnapshotIsActiveException>(() =>
            reviews.RecordAsync(zoneId, "v2", hashV2, "REJECT", "Must deactivate first."));
        await Assert.ThrowsAsync<ZoneGeometryActivationStateConflictException>(() =>
            activations.DeactivateAsync(zoneId, "v1", "Stale deactivation request."));

        var current = await activations.GetCurrentAsync(zoneId);
        var history = await activations.ListHistoryAsync(zoneId);
        Assert.NotNull(current);
        Assert.Equal("v2", current.GeometryVersion);
        Assert.Collection(history,
            first => { Assert.Equal("v1", first.GeometryVersion); Assert.Equal("ACTIVATE", first.Action); },
            second => { Assert.Equal("v1", second.GeometryVersion); Assert.Equal("DEACTIVATE", second.Action); },
            third => { Assert.Equal("v2", third.GeometryVersion); Assert.Equal("ACTIVATE", third.Action); });

        Assert.True(await activations.DeactivateAsync(zoneId, "v2", "Turn off while boundaries are reviewed."));
        await reviews.RecordAsync(zoneId, "v2", hashV2, "REJECT", "Reject after deactivation.");
        Assert.Null(await activations.GetCurrentAsync(zoneId));
        Assert.Equal(4, (await activations.ListHistoryAsync(zoneId)).Count);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var update = new NpgsqlCommand("""
            UPDATE zone_geometry_activation_events SET reason = 'tampered'
            WHERE zone_id = @zone_id;
            """, connection);
        update.Parameters.AddWithValue("zone_id", zoneId);
        await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());

        await using var delete = new NpgsqlCommand("""
            DELETE FROM zone_geometry_activation_events WHERE zone_id = @zone_id;
            """, connection);
        delete.Parameters.AddWithValue("zone_id", zoneId);
        await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());

        await using var truncate = new NpgsqlCommand("TRUNCATE zone_geometry_activation_events;", connection);
        await Assert.ThrowsAsync<PostgresException>(() => truncate.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Rejects_wrong_snapshot_hash_and_later_rejection()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var zoneId = "activation-" + Guid.NewGuid().ToString("N");
        await new PostgresZoneGeometryRepository(dataSource).PersistAsync(Boundary(zoneId, "v1", 2));
        var hash = await ReadSnapshotHashAsync(dataSource, zoneId, "v1");
        var context = new TestOperationsContext("zone-admin", MonoOperationsRoles.Administrator);
        var activations = new PostgresZoneGeometryActivationService(dataSource, context);
        var reviews = new PostgresZoneGeometryReviewService(dataSource, context);

        await reviews.RecordAsync(zoneId, "v1", hash, "APPROVE", "Reviewed.");
        await Assert.ThrowsAsync<ZoneGeometryActivationHashConflictException>(() =>
            activations.ActivateAsync(zoneId, "v1", new string('0', 64), "Wrong snapshot."));
        await reviews.RecordAsync(zoneId, "v1", hash, "REJECT", "Later review rejects this snapshot.");
        await Assert.ThrowsAsync<ZoneGeometryActivationReviewRequiredException>(() =>
            activations.ActivateAsync(zoneId, "v1", hash, "Must remain inactive."));
        Assert.Null(await activations.GetCurrentAsync(zoneId));
    }

    [Fact]
    public async Task Serializes_parallel_activation_changes_for_one_zone()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var zoneId = "activation-race-" + Guid.NewGuid().ToString("N");
        var snapshots = new PostgresZoneGeometryRepository(dataSource);
        await snapshots.PersistAsync(Boundary(zoneId, "v1", 2));
        await snapshots.PersistAsync(Boundary(zoneId, "v2", 3));
        var hashV1 = await ReadSnapshotHashAsync(dataSource, zoneId, "v1");
        var hashV2 = await ReadSnapshotHashAsync(dataSource, zoneId, "v2");
        var context = new TestOperationsContext("zone-admin", MonoOperationsRoles.Administrator);
        var reviews = new PostgresZoneGeometryReviewService(dataSource, context);
        await reviews.RecordAsync(zoneId, "v1", hashV1, "APPROVE", "Reviewed v1.");
        await reviews.RecordAsync(zoneId, "v2", hashV2, "APPROVE", "Reviewed v2.");

        var activations = new PostgresZoneGeometryActivationService(dataSource, context);
        var outcomes = await Task.WhenAll(
            activations.ActivateAsync(zoneId, "v1", hashV1, "Concurrent activation v1."),
            activations.ActivateAsync(zoneId, "v2", hashV2, "Concurrent activation v2."));
        var current = await activations.GetCurrentAsync(zoneId);
        var history = await activations.ListHistoryAsync(zoneId);

        Assert.Equal(2, outcomes.Length);
        Assert.NotNull(current);
        Assert.Contains(current.GeometryVersion, new[] { "v1", "v2" });
        Assert.Equal(3, history.Count);
        Assert.Single(history, entry => entry.Action == "DEACTIVATE");
        Assert.Equal(2, history.Count(entry => entry.Action == "ACTIVATE"));
        Assert.Equal(history[^1].GeometryVersion, current.GeometryVersion);
    }

    private static ZoneGeometryBoundary Boundary(string zoneId, string version, int edge)
    {
        var geometry = new WKTReader().Read(
            $"POLYGON ((0 0, {edge} 0, {edge} {edge}, 0 {edge}, 0 0))");
        geometry.SRID = 4326;
        return ZoneGeometryBoundary.Create(zoneId, ZoneKind.OperatingZone, version,
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

    private static async Task ApplyMigrationsAsync(NpgsqlDataSource dataSource)
    {
        foreach (var migration in new[]
        {
            "Mono.Api.Tests.Migrations.029_zone_geometry_versions.sql",
            "Mono.Api.Tests.Migrations.030_zone_geometry_reviews.sql",
            "Mono.Api.Tests.Migrations.031_zone_geometry_activation.sql"
        })
        {
            await using var stream = typeof(PostgresZoneGeometryActivationServiceTests).Assembly
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
