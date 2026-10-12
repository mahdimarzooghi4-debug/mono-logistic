using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using NetTopologySuite.IO;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class PostgresZoneGeometryReviewServiceTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Records_exact_snapshot_decisions_and_returns_ordered_history()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var zoneId = "review-" + Guid.NewGuid().ToString("N");
        var geometry = new WKTReader().Read("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))");
        geometry.SRID = 4326;
        var boundary = ZoneGeometryBoundary.Create(zoneId, ZoneKind.OperatingZone, "v1",
            "openstreetmap", "source-revision-1", "© OpenStreetMap contributors", geometry);
        var snapshots = new PostgresZoneGeometryRepository(dataSource);
        await snapshots.PersistAsync(boundary);

        string hash;
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var command = new NpgsqlCommand("""
            SELECT snapshot_sha256 FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = 'v1';
            """, connection))
        {
            command.Parameters.AddWithValue("zone_id", zoneId);
            hash = (string)(await command.ExecuteScalarAsync()
                ?? throw new InvalidOperationException("Snapshot hash was not stored."));
        }

        var service = new PostgresZoneGeometryReviewService(dataSource,
            new TestOperationsContext("reviewer-1", MonoOperationsRoles.Administrator));
        var approval = await service.RecordAsync(zoneId, "v1", hash, "approve", "Boundary checked.");
        var rejection = await service.RecordAsync(zoneId, "v1", hash, "reject", "Follow-up found an issue.");
        var history = await service.ListAsync(zoneId, "v1");

        Assert.Equal("APPROVE", approval.Decision);
        Assert.Equal("reviewer-1", approval.ActorId);
        Assert.Equal(hash, approval.SnapshotSha256);
        Assert.Equal("REJECT", rejection.Decision);
        Assert.NotNull(history);
        Assert.Collection(history,
            first => Assert.Equal(approval.ReviewId, first.ReviewId),
            second => Assert.Equal(rejection.ReviewId, second.ReviewId));
        Assert.Null(await service.ListAsync(zoneId, "missing-version"));

        await using var auditConnection = await dataSource.OpenConnectionAsync();
        await using var update = new NpgsqlCommand("""
            UPDATE zone_geometry_reviews SET reason = 'tampered'
            WHERE review_id = @review_id;
            """, auditConnection);
        update.Parameters.AddWithValue("review_id", approval.ReviewId);
        await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());

        await using var delete = new NpgsqlCommand("""
            DELETE FROM zone_geometry_reviews WHERE review_id = @review_id;
            """, auditConnection);
        delete.Parameters.AddWithValue("review_id", approval.ReviewId);
        await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());

        await using var truncate = new NpgsqlCommand("TRUNCATE zone_geometry_reviews;", auditConnection);
        await Assert.ThrowsAsync<PostgresException>(() => truncate.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task Rejects_stale_hash_and_non_administrator()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var zoneId = "review-" + Guid.NewGuid().ToString("N");
        var geometry = new WKTReader().Read("POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))");
        geometry.SRID = 4326;
        await new PostgresZoneGeometryRepository(dataSource).PersistAsync(
            ZoneGeometryBoundary.Create(zoneId, ZoneKind.OperatingZone, "v1",
                "openstreetmap", "source-revision-1", "© OpenStreetMap contributors", geometry));

        var service = new PostgresZoneGeometryReviewService(dataSource,
            new TestOperationsContext("reviewer-2", MonoOperationsRoles.Administrator));
        await Assert.ThrowsAsync<ZoneGeometrySnapshotHashConflictException>(() =>
            service.RecordAsync(zoneId, "v1", new string('0', 64), "APPROVE", "Review reason."));

        var unauthorized = new PostgresZoneGeometryReviewService(dataSource,
            new TestOperationsContext("viewer", "MONO_VIEWER"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            unauthorized.RecordAsync(zoneId, "v1", new string('0', 64), "APPROVE", "Review reason."));
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
            await using var stream = typeof(PostgresZoneGeometryReviewServiceTests).Assembly
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
