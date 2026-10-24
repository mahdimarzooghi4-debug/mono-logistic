using Mono.Api.Domain;
using Mono.Api.Persistence;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class PostgresRouteLocationCoordinateCatalogTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Versions_coordinates_replays_writes_and_resolves_only_current_heads()
    {
        var dataSource = await CreateDataSourceOrSkipAsync();
        if (dataSource is null)
            return;
        await using (dataSource)
        {
            await ApplyMigrationAsync(dataSource);
            var catalog = new PostgresRouteLocationCoordinateCatalog(dataSource);
            var locationId = Identifier("location");
            var create = new RouteLocationCoordinateWriteCommand(
                Identifier("command"), locationId, 0, new GeoCoordinate(35.7, 51.4));

            var persisted = await catalog.WriteAsync(create);
            var replay = await catalog.WriteAsync(create);
            var conflictingReplay = await catalog.WriteAsync(create with
            {
                Coordinate = new GeoCoordinate(35.8, 51.5)
            });
            var updated = await catalog.WriteAsync(new RouteLocationCoordinateWriteCommand(
                Identifier("command"), locationId, 1, new GeoCoordinate(35.8, 51.5)));
            var stale = await catalog.WriteAsync(new RouteLocationCoordinateWriteCommand(
                Identifier("command"), locationId, 1, new GeoCoordinate(35.9, 51.6)));
            var resolved = await catalog.ResolveAsync([locationId, Identifier("unknown")]);
            var history = await catalog.FindVersionAsync(locationId, 1);

            Assert.Equal(RouteLocationCoordinateWriteOutcome.Persisted, persisted.Outcome);
            Assert.Equal(1, persisted.Version);
            Assert.Equal(RouteLocationCoordinateWriteOutcome.Replayed, replay.Outcome);
            Assert.Equal(RouteLocationCoordinateWriteOutcome.Conflict, conflictingReplay.Outcome);
            Assert.Equal(RouteLocationCoordinateWriteOutcome.Persisted, updated.Outcome);
            Assert.Equal(2, updated.Version);
            Assert.Equal(RouteLocationCoordinateWriteOutcome.Conflict, stale.Outcome);
            Assert.Equal(new GeoCoordinate(35.8, 51.5), resolved[locationId]);
            Assert.Single(resolved);
            Assert.NotNull(history);
            Assert.Equal(new GeoCoordinate(35.7, 51.4), history.Coordinate);
            Assert.Null(await catalog.FindVersionAsync(locationId, 3));

            await using var connection = await dataSource.OpenConnectionAsync();
            await using var tamper = new NpgsqlCommand("""
                UPDATE route_location_coordinate_versions SET latitude = 0
                WHERE location_id = @location_id AND version = 1;
                """, connection);
            tamper.Parameters.AddWithValue("location_id", locationId);
            await Assert.ThrowsAsync<PostgresException>(() => tamper.ExecuteNonQueryAsync());
        }
    }

    [Fact]
    public async Task Serializes_competing_coordinate_writes_at_the_same_expected_version()
    {
        var dataSource = await CreateDataSourceOrSkipAsync();
        if (dataSource is null)
            return;
        await using (dataSource)
        {
            await ApplyMigrationAsync(dataSource);
            var catalog = new PostgresRouteLocationCoordinateCatalog(dataSource);
            var locationId = Identifier("location");
            var attempts = await Task.WhenAll(
                catalog.WriteAsync(new RouteLocationCoordinateWriteCommand(
                    Identifier("command"), locationId, 0, new GeoCoordinate(35.7, 51.4))),
                catalog.WriteAsync(new RouteLocationCoordinateWriteCommand(
                    Identifier("command"), locationId, 0, new GeoCoordinate(35.8, 51.5))));

            Assert.Single(attempts, result => result.Outcome == RouteLocationCoordinateWriteOutcome.Persisted);
            Assert.Single(attempts, result => result.Outcome == RouteLocationCoordinateWriteOutcome.Conflict);
            Assert.Single(await catalog.ResolveAsync([locationId]));
        }
    }

    [Fact]
    public void Rejects_invalid_identifiers_and_coordinates_before_storage()
    {
        Assert.Throws<ArgumentException>(() => RouteLocationCoordinateValidator.Validate(
            new RouteLocationCoordinateWriteCommand(" command", "loc", 0, new GeoCoordinate(35, 51))));
        Assert.Throws<ArgumentException>(() => RouteLocationCoordinateValidator.Validate(
            new RouteLocationCoordinateWriteCommand("command", "loc", 0, new GeoCoordinate(91, 51))));
    }

    private async Task<NpgsqlDataSource?> CreateDataSourceOrSkipAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return null;
        }
        return NpgsqlDataSource.Create(connectionString);
    }

    private static async Task ApplyMigrationAsync(NpgsqlDataSource dataSource)
    {
        var assembly = typeof(PostgresRouteLocationCoordinateCatalogTests).Assembly;
        await using var stream = assembly.GetManifestResourceStream(
            "Mono.Api.Tests.Migrations.053_route_location_coordinate_catalog.sql")
            ?? throw new InvalidOperationException("Missing route location coordinate catalog migration resource.");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static string Identifier(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
}
