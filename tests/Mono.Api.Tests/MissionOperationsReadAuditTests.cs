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
        await ExecuteMigrationAsync(dataSource, "024_mission_operations_read_audit.sql");
        await ExecuteMigrationAsync(dataSource, "025_mission_operations_read_audit_inspection.sql");
        await ExecuteMigrationAsync(dataSource, "051_mission_fare_review_audit.sql");
        await ExecuteMigrationAsync(dataSource, "087_mission_courier_issue_inbox_search.sql");
        await ExecuteMigrationAsync(dataSource, "088_mission_sla_alert_search.sql");
        await ExecuteMigrationAsync(dataSource, "089_mission_worker_status_read_audit.sql");
        await ExecuteMigrationAsync(dataSource, "092_operations_dashboard_summary_audit.sql");

        var actorId = "mission-read-audit-test-" + Guid.NewGuid().ToString("N");
        var missionId = "mis_0123456789abcdef0123456789abcdef";
        var service = new PostgresMissionOperationsReadAuditService(
            dataSource,
            new FixedOperationsContext(actorId, [MonoOperationsRoles.Administrator]));

        await service.RecordAsync(PostgresMissionOperationsReadAuditService.MissionSearch, null, 2);
        await service.RecordAsync(PostgresMissionOperationsReadAuditService.MissionDetail, missionId, 1);
        await service.RecordAsync(PostgresMissionOperationsReadAuditService.MissionTimeline, missionId, 0);

        var firstPage = await service.ListAsync(2, null, actorId, null, null, null);
        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);
        Assert.All(firstPage.Items, item => Assert.Equal(actorId, item.ActorId));
        var nextPage = await service.ListAsync(2, null, actorId, null, null, firstPage.NextCursor);
        Assert.NotNull(nextPage);
        Assert.Single(nextPage.Items);
        Assert.Equal(PostgresMissionOperationsReadAuditService.MissionSearch, nextPage.Items[0].Action);
        Assert.Null(await service.ListAsync(2, null, "other-staff", null, null, firstPage.NextCursor));

        var missionHistory = await service.ListAsync(10, missionId, null, null, null, null);
        Assert.NotNull(missionHistory);
        Assert.Equal(2, missionHistory.Items.Count);
        Assert.All(missionHistory.Items, item => Assert.Equal(missionId, item.MissionId));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ListAsync(10, null, actorId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1), null));

        await service.RecordAsync(PostgresMissionOperationsReadAuditService.MissionFareReview, null, 3);
        await service.RecordAsync(PostgresMissionOperationsReadAuditService.MissionCourierIssueInboxSearch, null, 0);
        await service.RecordAsync(PostgresMissionOperationsReadAuditService.MissionSlaAlertSearch, null, 1);
        await service.RecordAsync(PostgresMissionOperationsReadAuditService.DashboardSummary, null, 1);

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

            Assert.Equal(10, rows.Count);
            Assert.Equal(3, rows.Count(row => row.Action == "MISSION_AUDIT_INSPECTION"));
            Assert.Contains(rows, row => row == ("MISSION_SEARCH", null, 2));
            Assert.Contains(rows, row => row == ("MISSION_FARE_REVIEW", null, 3));
            Assert.Contains(rows, row => row == ("MISSION_COURIER_ISSUE_INBOX_SEARCH", null, 0));
            Assert.Contains(rows, row => row == ("MISSION_SLA_ALERT_SEARCH", null, 1));
            Assert.Contains(rows, row => row == ("DASHBOARD_SUMMARY", null, 1));
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

    [Fact]
    public async Task Mission_worker_status_read_is_recorded_in_an_isolated_audit_ledger()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var rootDataSource = NpgsqlDataSource.Create(connectionString);
        var schema = "mission_worker_audit_" + Guid.NewGuid().ToString("N");
        await using (var connection = await rootDataSource.OpenConnectionAsync())
        await using (var createSchema = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\";", connection))
            await createSchema.ExecuteNonQueryAsync();

        try
        {
            var isolatedConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
            {
                SearchPath = schema
            }.ConnectionString;
            await using var dataSource = NpgsqlDataSource.Create(isolatedConnectionString);
            foreach (var migration in new[]
                     {
                         "024_mission_operations_read_audit.sql",
                         "025_mission_operations_read_audit_inspection.sql",
                         "051_mission_fare_review_audit.sql",
                         "087_mission_courier_issue_inbox_search.sql",
                         "088_mission_sla_alert_search.sql",
                         "089_mission_worker_status_read_audit.sql"
                     })
                await ExecuteMigrationAsync(dataSource, migration);

            var actorId = "mission-worker-audit-" + Guid.NewGuid().ToString("N");
            var service = new PostgresMissionOperationsReadAuditService(
                dataSource, new FixedOperationsContext(actorId, [MonoOperationsRoles.Administrator]));
            await service.RecordAsync(PostgresMissionOperationsReadAuditService.MissionWorkerStatus,
                null, 1, CancellationToken.None);

            await using var connection = await dataSource.OpenConnectionAsync();
            await using var select = new NpgsqlCommand("""
                SELECT action, mission_id, result_count
                FROM mission_operations_read_audit WHERE actor_id = @actor_id;
                """, connection);
            select.Parameters.AddWithValue("actor_id", actorId);
            await using var reader = await select.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("MISSION_WORKER_STATUS", reader.GetString(0));
            Assert.True(reader.IsDBNull(1));
            Assert.Equal(1, reader.GetInt32(2));
            Assert.False(await reader.ReadAsync());
        }
        finally
        {
            await using var connection = await rootDataSource.OpenConnectionAsync();
            await using var dropSchema = new NpgsqlCommand($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE;", connection);
            await dropSchema.ExecuteNonQueryAsync();
        }
    }

    private static async Task ExecuteMigrationAsync(NpgsqlDataSource dataSource, string migrationFile)
    {
        var resourceName = "Mono.Api.Tests.Migrations." + migrationFile;
        await using var stream = typeof(MissionOperationsReadAuditTests).Assembly
            .GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Missing embedded migration resource: " + resourceName);
        using var reader = new StreamReader(stream);
        var sql = await reader.ReadToEndAsync();
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record FixedOperationsContext(string ActorId, IReadOnlyCollection<string> Roles)
        : IMonoOperationsContext;
}
