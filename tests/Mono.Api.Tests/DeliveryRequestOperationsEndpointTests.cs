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
        await ExecuteMigrationAsync(dataSource, "018_promise_commit.sql");
        await ExecuteMigrationAsync(dataSource, "019_delivery_quote_versions.sql");
        await ExecuteMigrationAsync(dataSource, "020_delivery_quote_issuance_idempotency.sql");
        await ExecuteMigrationAsync(dataSource, "021_mission_confirmation.sql");
        await ExecuteMigrationAsync(dataSource, "026_delivery_request_operations_read_audit.sql");

        var customerId = "cus_" + Guid.NewGuid().ToString("N");
        var branchId = "br_" + Guid.NewGuid().ToString("N");
        var secondBranchId = "br_" + Guid.NewGuid().ToString("N");
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

            await using (var secondBranch = new NpgsqlCommand("""
                INSERT INTO branches (branch_id, customer_id, name, status)
                VALUES (@branch_id, @customer_id, 'Operations request test branch 2', 'ACTIVE');
                """, connection))
            {
                secondBranch.Parameters.AddWithValue("branch_id", secondBranchId);
                secondBranch.Parameters.AddWithValue("customer_id", customerId);
                await secondBranch.ExecuteNonQueryAsync();
            }

            await InsertRequestAsync(connection, firstRequestId, customerId, branchId,
                "HANA-ORDER-100", "ops-request-test-1", createdAt);
            await InsertRequestAsync(connection, secondRequestId, customerId, secondBranchId,
                "HANA-ORDER-200", "ops-request-test-2", newerAt);
            await InsertMissionAsync(connection, customerId, firstRequestId, createdAt.AddHours(2));
        }

        var actorId = "ops-request-audit-" + Guid.NewGuid().ToString("N");
        var operations = new PostgresDeliveryRequestOperationsService(dataSource);
        var audit = new PostgresDeliveryRequestOperationsReadAuditService(
            dataSource, new FixedOperationsContext(actorId, [MonoOperationsRoles.Administrator]));

        var first = await operations.ListAsync(
            1, "RECEIVED", "hana-order-", createdAt, newerAt, null, null, null, null);
        Assert.NotNull(first);
        Assert.Single(first.Items);
        Assert.Equal(secondRequestId, first.Items[0].DeliveryRequestId);
        Assert.Equal("HANA-ORDER-200", first.Items[0].ExternalReference);
        Assert.Equal(customerId, first.Items[0].CustomerId);
        Assert.Equal(secondBranchId, first.Items[0].BranchId);
        Assert.NotNull(first.NextCursor);

        var second = await operations.ListAsync(
            1, "RECEIVED", "hana-order-", createdAt, newerAt, null, null, null, first.NextCursor);
        Assert.NotNull(second);
        Assert.Single(second.Items);
        Assert.Equal(firstRequestId, second.Items[0].DeliveryRequestId);
        Assert.Equal("mis_", second.Items[0].MissionId![..4]);
        Assert.Equal("PROMISED", second.Items[0].MissionStatus);
        Assert.NotNull(second.Items[0].CommittedDeliveryAt);
        Assert.Null(await operations.ListAsync(
            1, "RECEIVED", "hana-order-", createdAt, createdAt, null, null, null, first.NextCursor));
        Assert.Null(await operations.ListAsync(
            1, "RECEIVED", null, null, null, customerId, branchId, true, first.NextCursor));

        var confirmedOnly = await operations.ListAsync(
            10, null, null, null, null, customerId, branchId, true, null);
        Assert.NotNull(confirmedOnly);
        Assert.Single(confirmedOnly.Items);
        Assert.Equal(firstRequestId, confirmedOnly.Items[0].DeliveryRequestId);
        var unconfirmedOnSecondBranch = await operations.ListAsync(
            10, null, null, null, null, customerId, secondBranchId, false, null);
        Assert.NotNull(unconfirmedOnSecondBranch);
        Assert.Single(unconfirmedOnSecondBranch.Items);
        Assert.Equal(secondRequestId, unconfirmedOnSecondBranch.Items[0].DeliveryRequestId);
        var wrongCustomer = await operations.ListAsync(
            10, null, null, null, null, "cus_ffffffffffffffffffffffffffffffff", null, null, null);
        Assert.NotNull(wrongCustomer);
        Assert.Empty(wrongCustomer.Items);

        var detail = await operations.FindByIdAsync(firstRequestId);
        Assert.NotNull(detail);
        Assert.Equal("HANA-ORDER-100", detail.ExternalReference);
        Assert.Equal("PROMISED", detail.MissionStatus);
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

    private static async Task InsertMissionAsync(
        NpgsqlConnection connection,
        string customerId,
        string requestId,
        DateTimeOffset committedAt)
    {
        var quoteId = "qte_" + Guid.NewGuid().ToString("N");
        var promiseId = "prm_" + Guid.NewGuid().ToString("N");
        var missionId = "mis_" + Guid.NewGuid().ToString("N");
        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        var decisionId = "decision-" + Guid.NewGuid().ToString("N");
        await using (var quote = new NpgsqlCommand("""
            INSERT INTO delivery_quote_versions (
                customer_id, delivery_request_id, quote_id, version, issued_at, expires_at,
                snapshot_hash, snapshot, created_at)
            VALUES (@customer_id, @request_id, @quote_id, 1, @issued_at, @expires_at,
                @hash, '{}'::jsonb, @issued_at);
            """, connection))
        {
            quote.Parameters.AddWithValue("customer_id", customerId);
            quote.Parameters.AddWithValue("request_id", requestId);
            quote.Parameters.AddWithValue("quote_id", quoteId);
            quote.Parameters.AddWithValue("issued_at", committedAt.AddHours(-1));
            quote.Parameters.AddWithValue("expires_at", committedAt.AddHours(1));
            quote.Parameters.AddWithValue("hash", new string('a', 64));
            await quote.ExecuteNonQueryAsync();
        }

        await using (var promise = new NpgsqlCommand("""
            INSERT INTO promises (
                promise_id, customer_id, delivery_request_id, current_version, latest_decision_id,
                committed_delivery_at, outcome, safety_buffer_ticks, safety_buffer_policy_version,
                policy_versions, reason_codes, updated_at)
            VALUES (@promise_id, @customer_id, @request_id, 1, @decision_id,
                @committed_at, 'ACCEPTED', 0, 'test-policy', '{}'::jsonb, '[]'::jsonb, @committed_at);
            """, connection))
        {
            promise.Parameters.AddWithValue("promise_id", promiseId);
            promise.Parameters.AddWithValue("customer_id", customerId);
            promise.Parameters.AddWithValue("request_id", requestId);
            promise.Parameters.AddWithValue("decision_id", decisionId);
            promise.Parameters.AddWithValue("committed_at", committedAt);
            await promise.ExecuteNonQueryAsync();
        }

        await using (var version = new NpgsqlCommand("""
            INSERT INTO promise_versions (
                promise_id, customer_id, version, decision_id, proposal_hash, proposal, event_id, created_at)
            VALUES (@promise_id, @customer_id, 1, @decision_id, @hash,
                jsonb_build_object('delivery_request_id', @request_id), @event_id, @created_at);
            """, connection))
        {
            version.Parameters.AddWithValue("promise_id", promiseId);
            version.Parameters.AddWithValue("customer_id", customerId);
            version.Parameters.AddWithValue("request_id", requestId);
            version.Parameters.AddWithValue("decision_id", decisionId);
            version.Parameters.AddWithValue("hash", new string('b', 64));
            version.Parameters.AddWithValue("event_id", eventId);
            version.Parameters.AddWithValue("created_at", committedAt);
            await version.ExecuteNonQueryAsync();
        }

        await using var mission = new NpgsqlCommand("""
            INSERT INTO missions (
                mission_id, customer_id, delivery_request_id, quote_id, quote_version,
                promise_id, promise_version, status, committed_delivery_at, created_at)
            VALUES (@mission_id, @customer_id, @request_id, @quote_id, 1,
                @promise_id, 1, 'PROMISED', @committed_at, @created_at);
            """, connection);
        mission.Parameters.AddWithValue("mission_id", missionId);
        mission.Parameters.AddWithValue("customer_id", customerId);
        mission.Parameters.AddWithValue("request_id", requestId);
        mission.Parameters.AddWithValue("quote_id", quoteId);
        mission.Parameters.AddWithValue("promise_id", promiseId);
        mission.Parameters.AddWithValue("committed_at", committedAt);
        mission.Parameters.AddWithValue("created_at", committedAt);
        await mission.ExecuteNonQueryAsync();
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
