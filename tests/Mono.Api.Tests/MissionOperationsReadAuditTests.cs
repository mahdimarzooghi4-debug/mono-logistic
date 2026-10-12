using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class MissionOperationsReadAuditTests
{
    [Fact]
    public async Task Privileged_mission_reads_are_recorded_without_search_terms_and_cannot_be_mutated()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using (var migration = await dataSource.OpenConnectionAsync())
        await using (var command = new NpgsqlCommand(await ReadMigrationAsync(), migration))
            await command.ExecuteNonQueryAsync();

        var actorId = "mission-read-audit-test-" + Guid.NewGuid().ToString("N");
        var missionId = "mis_0123456789abcdef0123456789abcdef";
        var service = new PostgresMissionOperationsReadAuditService(
            dataSource,
            new FixedOperationsContext(actorId, [MonoOperationsRoles.Administrator]));

        await service.RecordAsync(PostgresMissionOperationsReadAuditService.MissionSearch, null, 2);
        await service.RecordAsync(PostgresMissionOperationsReadAuditService.MissionDetail, missionId, 1);
        await service.RecordAsync(PostgresMissionOperationsReadAuditService.MissionTimeline, missionId, 0);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using (var select = new NpgsqlCommand("""
            SELECT action, mission_id, result_count
            FROM mission_operations_read_audit
            WHERE actor_id = @actor_id
            ORDER BY action;
            """, connection))
        {
            select.Parameters.AddWithValue("actor_id", actorId);
            await using var reader = await select.ExecuteReaderAsync();
            var rows = new List<(string Action, string? MissionId, int Count)>();
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt32(2)));

            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, row => row == ("MISSION_SEARCH", null, 2));
            Assert.Contains(rows, row => row == ("MISSION_DETAIL", missionId, 1));
            Assert.Contains(rows, row => row == ("MISSION_TIMELINE", missionId, 0));
        }

        await using var mutate = new NpgsqlCommand("""
            UPDATE mission_operations_read_audit SET result_count = 99 WHERE actor_id = @actor_id;
            """, connection);
        mutate.Parameters.AddWithValue("actor_id", actorId);
        await Assert.ThrowsAsync<PostgresException>(() => mutate.ExecuteNonQueryAsync());

        var nonAdmin = new PostgresMissionOperationsReadAuditService(
            dataSource, new FixedOperationsContext(actorId, ["MONO_OPERATOR"]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            nonAdmin.RecordAsync(PostgresMissionOperationsReadAuditService.MissionSearch, null, 0));
    }

    private static async Task<string> ReadMigrationAsync()
    {
        await using var stream = typeof(MissionOperationsReadAuditTests).Assembly
            .GetManifestResourceStream("Mono.Api.Tests.Migrations.024_mission_operations_read_audit.sql")
            ?? throw new InvalidOperationException("Missing embedded Mission operations read audit migration.");
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private sealed record FixedOperationsContext(string ActorId, IReadOnlyCollection<string> Roles)
        : IMonoOperationsContext;
}
