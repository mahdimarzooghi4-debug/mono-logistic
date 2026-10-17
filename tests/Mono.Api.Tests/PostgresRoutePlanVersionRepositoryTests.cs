using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class PostgresRoutePlanVersionRepositoryTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset StartedAt = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Route_plan_reads_and_audit_inspection_are_scoped_paginated_and_append_only()
    {
        var dataSource = await CreateDataSourceOrSkipAsync();
        if (dataSource is null)
            return;
        await using (dataSource)
        {
            await ApplyMigrationAsync(dataSource);
            var routeId = Identifier("route-audit");
            var repository = new PostgresRoutePlanVersionRepository(dataSource);
            var snapshot = Snapshot(routeId, "mission-audit", "pickup", "dropoff", StartedAt.AddMinutes(30));
            Assert.Equal(RoutePlanVersionWriteOutcome.Persisted,
                (await repository.PersistAsync(new RoutePlanVersionWriteCommand(
                    Identifier("cmd"), 0, snapshot))).Outcome);

            var now = DateTimeOffset.UtcNow;
            var context = new TestOperationsContext();
            var audit = new PostgresRoutePlanOperationsReadAuditService(dataSource, context);
            await audit.RecordAsync(PostgresRoutePlanOperationsReadAuditService.CurrentPlanRead,
                routeId, 1, "FOUND", 1);
            await audit.RecordAsync(PostgresRoutePlanOperationsReadAuditService.PlanVersionRead,
                routeId, 1, "FOUND", 1);
            await audit.RecordAsync(PostgresRoutePlanOperationsReadAuditService.PlanVersionRead,
                routeId, 9, "NOT_FOUND", 0);

            var first = await audit.ListAsync(routeId, null, null,
                now.AddMinutes(-2), now.AddMinutes(2), 2, null);
            Assert.Equal(2, first!.Items.Count);
            Assert.NotNull(first.NextCursor);
            var second = await audit.ListAsync(routeId, null, null,
                now.AddMinutes(-2), now.AddMinutes(2), 2, first.NextCursor);
            Assert.Single(second!.Items);
            Assert.Null(second.NextCursor);
            Assert.Equal(3, first.Items.Concat(second.Items).Count(item => item.RouteId == routeId));
            Assert.Contains(first.Items.Concat(second.Items), item =>
                item.Action == PostgresRoutePlanOperationsReadAuditService.CurrentPlanRead &&
                item.Version == 1 && item.Outcome == "FOUND");
            Assert.Contains(first.Items.Concat(second.Items), item =>
                item.Action == PostgresRoutePlanOperationsReadAuditService.PlanVersionRead &&
                item.Version == 9 && item.Outcome == "NOT_FOUND");

            var missingOnly = await audit.ListAsync(routeId, null, "NOT_FOUND",
                now.AddMinutes(-2), now.AddMinutes(2), 10, null);
            Assert.Single(missingOnly!.Items);
            var inspections = await audit.ListAsync(null,
                PostgresRoutePlanOperationsReadAuditService.AuditInspection, "FOUND",
                now.AddMinutes(-2), now.AddMinutes(2), 10, null);
            Assert.Equal(3, inspections!.Items.Count);

            await using var connection = await dataSource.OpenConnectionAsync();
            await using var update = new NpgsqlCommand(
                "UPDATE route_plan_operations_read_audit SET outcome = 'TAMPERED' WHERE route_id = @route_id;",
                connection);
            update.Parameters.AddWithValue("route_id", routeId);
            await Assert.ThrowsAsync<PostgresException>(async () => { await update.ExecuteNonQueryAsync(); });
        }
    }

    private sealed class TestOperationsContext : IMonoOperationsContext
    {
        public string ActorId => "route-audit-operator";
        public IReadOnlyCollection<string> Roles => [MonoOperationsRoles.Administrator];
    }

    [Fact]
    public async Task Persists_immutable_route_versions_and_replays_idempotently()
    {
        var dataSource = await CreateDataSourceOrSkipAsync();
        if (dataSource is null)
            return;
        await using (dataSource)
        {
            await ApplyMigrationAsync(dataSource);
            var repository = new PostgresRoutePlanVersionRepository(dataSource);
            var routeId = Identifier("route");
            var first = Snapshot(routeId, "mission-1", "A", "B", StartedAt.AddMinutes(30));
            var firstCommand = new RoutePlanVersionWriteCommand(Identifier("cmd"), 0, first);

            var created = await repository.PersistAsync(firstCommand);
            var replay = await repository.PersistAsync(firstCommand);
            var readFirst = await repository.FindVersionAsync(routeId, 1);
            var second = InsertMission(first, "mission-2", "C", "D", StartedAt.AddMinutes(35));
            var secondCommand = new RoutePlanVersionWriteCommand(Identifier("cmd"), 1, second);
            var added = await repository.PersistAsync(secondCommand);
            var current = await repository.FindCurrentAsync(routeId);
            var history = await repository.FindVersionAsync(routeId, 1);

            Assert.Equal(RoutePlanVersionWriteOutcome.Persisted, created.Outcome);
            Assert.Equal(1, created.Version);
            Assert.Equal(RoutePlanVersionWriteOutcome.Replayed, replay.Outcome);
            Assert.NotNull(readFirst);
            AssertSnapshot(first, readFirst.Snapshot);
            Assert.Equal(RoutePlanVersionWriteOutcome.Persisted, added.Outcome);
            Assert.Equal(2, added.Version);
            Assert.NotNull(current);
            Assert.Equal(2, current.Version);
            AssertSnapshot(second, current.Snapshot);
            Assert.NotNull(history);
            Assert.Equal(1, history.Version);
            AssertSnapshot(first, history.Snapshot);

            var changedRetry = second with
            {
                OrderedStops =
                [
                    second.OrderedStops[0],
                    second.OrderedStops[1] with { LocationId = "changed" },
                    .. second.OrderedStops.Skip(2)
                ]
            };
            var changedReplay = await repository.PersistAsync(secondCommand with { Snapshot = changedRetry });
            var stale = await repository.PersistAsync(new RoutePlanVersionWriteCommand(
                Identifier("cmd"), 1, second));
            Assert.Equal(RoutePlanVersionWriteOutcome.Conflict, changedReplay.Outcome);
            Assert.Equal(RoutePlanVersionWriteOutcome.Conflict, stale.Outcome);

            await using var connection = await dataSource.OpenConnectionAsync();
            await using var update = new NpgsqlCommand("""
                UPDATE route_plan_version_stops SET location_id = 'tampered'
                WHERE route_id = @route_id AND version = 1 AND stop_order = 0;
                """, connection);
            update.Parameters.AddWithValue("route_id", routeId);
            await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());

            await using var delete = new NpgsqlCommand("""
                DELETE FROM route_plan_version_missions WHERE route_id = @route_id AND version = 1;
                """, connection);
            delete.Parameters.AddWithValue("route_id", routeId);
            await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());

            var intact = await repository.FindVersionAsync(routeId, 1);
            Assert.NotNull(intact);
            AssertSnapshot(first, intact.Snapshot);
        }
    }

    [Fact]
    public async Task Serializes_competing_insertions_against_the_same_route_version()
    {
        var dataSource = await CreateDataSourceOrSkipAsync();
        if (dataSource is null)
            return;
        await using (dataSource)
        {
            await ApplyMigrationAsync(dataSource);
            var repository = new PostgresRoutePlanVersionRepository(dataSource);
            var routeId = Identifier("route");
            var first = Snapshot(routeId, "mission-1", "A", "B", StartedAt.AddMinutes(30));
            Assert.Equal(RoutePlanVersionWriteOutcome.Persisted,
                (await repository.PersistAsync(new RoutePlanVersionWriteCommand(Identifier("cmd"), 0, first))).Outcome);

            var candidateA = InsertMission(first, "mission-2a", "C", "D", StartedAt.AddMinutes(40));
            var candidateB = InsertMission(first, "mission-2b", "E", "F", StartedAt.AddMinutes(40));
            var attempts = await Task.WhenAll(
                repository.PersistAsync(new RoutePlanVersionWriteCommand(Identifier("cmd"), 1, candidateA)),
                repository.PersistAsync(new RoutePlanVersionWriteCommand(Identifier("cmd"), 1, candidateB)));

            Assert.Single(attempts, result => result.Outcome == RoutePlanVersionWriteOutcome.Persisted);
            Assert.Single(attempts, result => result.Outcome == RoutePlanVersionWriteOutcome.Conflict);
            var current = await repository.FindCurrentAsync(routeId);
            Assert.NotNull(current);
            Assert.Equal(2, current.Version);
            Assert.Equal(2, current.Snapshot.Missions.Count);
        }
    }

    [Fact]
    public async Task Rejects_route_evolution_that_changes_an_existing_mission_promise()
    {
        var dataSource = await CreateDataSourceOrSkipAsync();
        if (dataSource is null)
            return;
        await using (dataSource)
        {
            await ApplyMigrationAsync(dataSource);
            var repository = new PostgresRoutePlanVersionRepository(dataSource);
            var routeId = Identifier("route");
            var first = Snapshot(routeId, "mission-1", "A", "B", StartedAt.AddMinutes(30));
            await repository.PersistAsync(new RoutePlanVersionWriteCommand(Identifier("cmd"), 0, first));
            var changedDeadline = InsertMission(first, "mission-2", "C", "D", StartedAt.AddMinutes(40)) with
            {
                Missions =
                [
                    new RouteMissionDeadline("mission-1", StartedAt.AddMinutes(31)),
                    new RouteMissionDeadline("mission-2", StartedAt.AddMinutes(40))
                ]
            };

            await Assert.ThrowsAsync<ArgumentException>(() => repository.PersistAsync(
                new RoutePlanVersionWriteCommand(Identifier("cmd"), 1, changedDeadline)));
            var current = await repository.FindCurrentAsync(routeId);
            Assert.NotNull(current);
            Assert.Equal(1, current.Version);
        }
    }

    [Fact]
    public async Task Existing_version_write_for_unknown_route_returns_not_found()
    {
        var dataSource = await CreateDataSourceOrSkipAsync();
        if (dataSource is null)
            return;
        await using (dataSource)
        {
            await ApplyMigrationAsync(dataSource);
            var repository = new PostgresRoutePlanVersionRepository(dataSource);
            var routeId = Identifier("missing-route");
            var result = await repository.PersistAsync(new RoutePlanVersionWriteCommand(
                Identifier("cmd"), 1,
                Snapshot(routeId, "mission-1", "A", "B", StartedAt.AddMinutes(20))));

            Assert.Equal(RoutePlanVersionWriteOutcome.RouteNotFound, result.Outcome);
            Assert.Null(await repository.FindCurrentAsync(routeId));
        }
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

    private static RoutePlanSnapshot Snapshot(
        string routeId,
        string missionId,
        string pickup,
        string dropoff,
        DateTimeOffset deadline) => new(
            routeId,
            "start",
            StartedAt,
            [new RouteMissionDeadline(missionId, deadline)],
            [
                new RouteInsertionStop(missionId, RouteStopKind.Pickup, pickup, 15, StartedAt),
                new RouteInsertionStop(missionId, RouteStopKind.Dropoff, dropoff, 30)
            ]);

    private static RoutePlanSnapshot InsertMission(
        RoutePlanSnapshot current,
        string missionId,
        string pickup,
        string dropoff,
        DateTimeOffset deadline) => current with
        {
            Missions = current.Missions.Append(new RouteMissionDeadline(missionId, deadline)).ToArray(),
            OrderedStops =
            [
                current.OrderedStops[0],
                new RouteInsertionStop(missionId, RouteStopKind.Pickup, pickup, 15, StartedAt),
                new RouteInsertionStop(missionId, RouteStopKind.Dropoff, dropoff, 30),
                .. current.OrderedStops.Skip(1)
            ]
        };

    private static string Identifier(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private static void AssertSnapshot(RoutePlanSnapshot expected, RoutePlanSnapshot actual)
    {
        Assert.Equal(expected.RouteId, actual.RouteId);
        Assert.Equal(expected.StartLocationId, actual.StartLocationId);
        Assert.Equal(expected.StartedAt, actual.StartedAt);
        Assert.Equal(expected.Missions.ToArray(), actual.Missions.ToArray());
        Assert.Equal(expected.OrderedStops.ToArray(), actual.OrderedStops.ToArray());
    }

    private static async Task ApplyMigrationAsync(NpgsqlDataSource dataSource)
    {
        var assembly = typeof(PostgresRoutePlanVersionRepositoryTests).Assembly;
        await using var stream = assembly.GetManifestResourceStream(
            "Mono.Api.Tests.Migrations.052_route_plan_versions.sql")
            ?? throw new InvalidOperationException("Missing route plan migration resource.");
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
        await using var auditStream = assembly.GetManifestResourceStream(
            "Mono.Api.Tests.Migrations.086_route_plan_operations_read_audit.sql")
            ?? throw new InvalidOperationException("Missing route plan read audit migration resource.");
        using var auditReader = new StreamReader(auditStream);
        var auditSql = await auditReader.ReadToEndAsync();
        await using var auditCommand = new NpgsqlCommand(auditSql, connection);
        await auditCommand.ExecuteNonQueryAsync();
    }
}
