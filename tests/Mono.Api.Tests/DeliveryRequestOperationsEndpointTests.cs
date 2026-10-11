using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class DeliveryRequestOperationsEndpointTests
{
    [Fact]
    public async Task Operations_request_search_is_filterable_paged_private_and_audited()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ExecuteMigrationAsync(dataSource, "001_customer_branch.sql");
        await ExecuteMigrationAsync(dataSource, "003_delivery_request_intake.sql");
        await ExecuteMigrationAsync(dataSource, "004_delivery_request_list_index.sql");
        await ExecuteMigrationAsync(dataSource, "026_delivery_request_operations_read_audit.sql");

        var customerId = "cus_" + Guid.NewGuid().ToString("N");
        var branchId = "br_" + Guid.NewGuid().ToString("N");
        var firstRequestId = "drq_" + Guid.NewGuid().ToString("N");
        var secondRequestId = "drq_" + Guid.NewGuid().ToString("N");
        var createdAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var newerAt = createdAt.AddMinutes(1);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await using (var customer = new NpgsqlCommand("""
                INSERT INTO customers (customer_id, legal_name, status, created_at)
                VALUES (@customer_id, 'Operations request test', 'ACTIVE', @created_at);
                """, connection))
            {
                customer.Parameters.AddWithValue("customer_id", customerId);
                customer.Parameters.AddWithValue("created_at", createdAt);
                await customer.ExecuteNonQueryAsync();
            }

            await using (var branch = new NpgsqlCommand("""
                INSERT INTO branches (branch_id, customer_id, name, status)
                VALUES (@branch_id, @customer_id, 'Operations request test branch', 'ACTIVE');
                """, connection))
            {
                branch.Parameters.AddWithValue("branch_id", branchId);
                branch.Parameters.AddWithValue("customer_id", customerId);
                await branch.ExecuteNonQueryAsync();
            }

            await InsertRequestAsync(connection, firstRequestId, customerId, branchId,
                "HANA-ORDER-100", "ops-request-test-1", createdAt);
            await InsertRequestAsync(connection, secondRequestId, customerId, branchId,
                "HANA-ORDER-200", "ops-request-test-2", newerAt);
        }

        var actorId = "ops-request-audit-" + Guid.NewGuid().ToString("N");
        var operations = new PostgresDeliveryRequestOperationsService(dataSource);
        var audit = new PostgresDeliveryRequestOperationsReadAuditService(
            dataSource, new FixedOperationsContext(actorId, [MonoOperationsRoles.Administrator]));

        var first = await operations.ListAsync(1, "RECEIVED", "hana-order-", createdAt, newerAt, null);
        Assert.NotNull(first);
        Assert.Single(first.Items);
        Assert.Equal(secondRequestId, first.Items[0].DeliveryRequestId);
        Assert.Equal("HANA-ORDER-200", first.Items[0].ExternalReference);
        Assert.Equal(customerId, first.Items[0].CustomerId);
        Assert.Equal(branchId, first.Items[0].BranchId);
        Assert.NotNull(first.NextCursor);

        var second = await operations.ListAsync(
            1, "RECEIVED", "hana-order-", createdAt, newerAt, first.NextCursor);
        Assert.NotNull(second);
        Assert.Single(second.Items);
        Assert.Equal(firstRequestId, second.Items[0].DeliveryRequestId);
        Assert.Null(await operations.ListAsync(
            1, "RECEIVED", "hana-order-", createdAt, createdAt, first.NextCursor));

        var detail = await operations.FindByIdAsync(firstRequestId);
        Assert.NotNull(detail);
        Assert.Equal("HANA-ORDER-100", detail.ExternalReference);
        Assert.Null(await operations.FindByIdAsync("drq_ffffffffffffffffffffffffffffffff"));

        await audit.RecordAsync(PostgresDeliveryRequestOperationsReadAuditService.Search, null, first.Items.Count);
        await audit.RecordAsync(PostgresDeliveryRequestOperationsReadAuditService.Detail, firstRequestId, 1);
        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await using var select = new NpgsqlCommand("""
                SELECT action, delivery_request_id, result_count
                FROM delivery_request_operations_read_audit
                WHERE actor_id = @actor_id
                ORDER BY action;
                """, connection);
            select.Parameters.AddWithValue("actor_id", actorId);
            await using var reader = await select.ExecuteReaderAsync();
            var rows = new List<(string Action, string? RequestId, int Count)>();
            while (await reader.ReadAsync())
                rows.Add((reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt32(2)));
            Assert.Equal(2, rows.Count);
            Assert.Contains(rows, row => row == ("DELIVERY_REQUEST_SEARCH", null, 1));
            Assert.Contains(rows, row => row == ("DELIVERY_REQUEST_DETAIL", firstRequestId, 1));
        }

        var nonAdminAudit = new PostgresDeliveryRequestOperationsReadAuditService(
            dataSource, new FixedOperationsContext(actorId, ["MONO_OPERATOR"]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            nonAdminAudit.RecordAsync(PostgresDeliveryRequestOperationsReadAuditService.Search, null, 0));
    }

    private static async Task InsertRequestAsync(
        NpgsqlConnection connection,
        string requestId,
        string customerId,
        string branchId,
        string externalReference,
        string idempotencyKey,
        DateTimeOffset createdAt)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO delivery_requests (
                delivery_request_id, customer_id, branch_id, external_reference,
                idempotency_key, request_hash, request_payload, status, created_at)
            VALUES (
                @request_id, @customer_id, @branch_id, @external_reference,
                @idempotency_key, @request_hash, '{}'::jsonb, 'RECEIVED', @created_at);
            """, connection);
        command.Parameters.AddWithValue("request_id", requestId);
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("branch_id", branchId);
        command.Parameters.AddWithValue("external_reference", externalReference);
        command.Parameters.AddWithValue("idempotency_key", idempotencyKey);
        command.Parameters.AddWithValue("request_hash", new string('a', 64));
        command.Parameters.AddWithValue("created_at", createdAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ExecuteMigrationAsync(NpgsqlDataSource dataSource, string migrationFile)
    {
        var resourceName = "Mono.Api.Tests.Migrations." + migrationFile;
        await using var stream = typeof(DeliveryRequestOperationsEndpointTests).Assembly
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
