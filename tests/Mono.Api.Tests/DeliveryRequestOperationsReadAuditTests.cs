using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class DeliveryRequestOperationsReadAuditTests
{
    [Fact]
    public async Task Delivery_request_read_audit_is_filterable_paged_self_audited_and_immutable()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ExecuteMigrationAsync(dataSource, "026_delivery_request_operations_read_audit.sql");
        await ExecuteMigrationAsync(dataSource, "027_delivery_request_operations_read_audit_inspection.sql");

        var actorId = "delivery-request-audit-test-" + Guid.NewGuid().ToString("N");
        var requestId = "drq_" + Guid.NewGuid().ToString("N");
        var service = new PostgresDeliveryRequestOperationsReadAuditService(
            dataSource,
            new FixedOperationsContext(actorId, [MonoOperationsRoles.Administrator]));

        await service.RecordAsync(PostgresDeliveryRequestOperationsReadAuditService.Search, null, 2);
        await service.RecordAsync(PostgresDeliveryRequestOperationsReadAuditService.Detail, requestId, 1);
        await service.RecordAsync(PostgresDeliveryRequestOperationsReadAuditService.Search, null, 0);

        var firstPage = await service.ListAsync(1, null, actorId, null, null, null);
        Assert.NotNull(firstPage);
        Assert.Single(firstPage.Items);
        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(actorId, firstPage.Items[0].ActorId);

        var secondPage = await service.ListAsync(1, null, actorId, null, null, firstPage.NextCursor);
        Assert.NotNull(secondPage);
        Assert.Single(secondPage.Items);
        Assert.NotEqual(firstPage.Items[0].AuditId, secondPage.Items[0].AuditId);
        Assert.Null(await service.ListAsync(1, null, "another-operator", null, null, firstPage.NextCursor));

        var requestHistory = await service.ListAsync(10, requestId, null, null, null, null);
        Assert.NotNull(requestHistory);
        Assert.Single(requestHistory.Items);
        Assert.Equal(requestId, requestHistory.Items[0].DeliveryRequestId);
        Assert.Equal(PostgresDeliveryRequestOperationsReadAuditService.Detail, requestHistory.Items[0].Action);
        Assert.Equal(1, requestHistory.Items[0].ResultCount);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ListAsync(10, null, actorId, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(-1), null));

        await using var connection = await dataSource.OpenConnectionAsync();
        await using (var select = new NpgsqlCommand("""
            SELECT action, delivery_request_id, result_count
            FROM delivery_request_operations_read_audit
            WHERE actor_id = @actor_id;
            """, connection))
        {
            select.Parameters.AddWithValue("actor_id", actorId);
            await using var reader = await select.ExecuteReaderAsync();
            var rows = new List<(string Action, string? RequestId, int Count)>();
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt32(2)));

            Assert.Equal(6, rows.Count);
            Assert.Equal(3, rows.Count(row =>
                row.Action == PostgresDeliveryRequestOperationsReadAuditService.AuditInspection));
            Assert.Contains(rows, row =>
                row == (PostgresDeliveryRequestOperationsReadAuditService.Search, null, 2));
            Assert.Contains(rows, row =>
                row == (PostgresDeliveryRequestOperationsReadAuditService.Detail, requestId, 1));
            Assert.Contains(rows, row =>
                row == (PostgresDeliveryRequestOperationsReadAuditService.Search, null, 0));
        }

        await using var mutate = new NpgsqlCommand("""
            UPDATE delivery_request_operations_read_audit
            SET result_count = 99
            WHERE actor_id = @actor_id;
            """, connection);
        mutate.Parameters.AddWithValue("actor_id", actorId);
        await Assert.ThrowsAsync<PostgresException>(() => mutate.ExecuteNonQueryAsync());

        var nonAdmin = new PostgresDeliveryRequestOperationsReadAuditService(
            dataSource, new FixedOperationsContext(actorId, ["MONO_OPERATOR"]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            nonAdmin.ListAsync(10, null, null, null, null, null));
    }

    private static async Task ExecuteMigrationAsync(NpgsqlDataSource dataSource, string migrationFile)
    {
        var resourceName = "Mono.Api.Tests.Migrations." + migrationFile;
        await using var stream = typeof(DeliveryRequestOperationsReadAuditTests).Assembly
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
