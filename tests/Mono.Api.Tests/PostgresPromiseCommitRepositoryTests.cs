using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class PostgresPromiseCommitRepositoryTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Commit_revision_and_idempotent_replay_are_atomic_and_tenant_scoped()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var customer = Customer.Create("Mono Promise Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        var branch = Branch.Create(customer.CustomerId, "Promise Test Branch", "ACTIVE");
        await InsertBranchAsync(dataSource, branch);
        var context = new FixedCustomerContext(customer.CustomerId);
        var intake = new PostgresDeliveryRequestRepository(dataSource, context);
        var requestPayload = new DeliveryRequestInput
        {
            ExternalReference = "PROMISE-1",
            BranchId = branch.BranchId,
            Pickup = new DeliveryLocationInput { AddressText = "Store" },
            Dropoff = new DeliveryLocationInput { AddressText = "Customer" },
            Shipment = new DeliveryShipmentInput { ShipmentModel = "BAG_BASED", BagCount = 1, LoadClass = "S" },
            RequestedService = "URBAN_DELIVERY",
            RequestedPriority = "STANDARD"
        }.Normalize().ToCanonicalJson();
        var request = await intake.CreateOrReplayAsync(branch.BranchId, "PROMISE-1", "promise-intake-1", requestPayload);
        var repository = new PostgresPromiseCommitRepository(dataSource, context);
        var initial = Proposal(request.DeliveryRequestId, 1, "decision-1");
        var committed = await repository.CommitAsync(initial);
        var replayed = await repository.CommitAsync(initial);
        var revised = await repository.CommitAsync(Proposal(request.DeliveryRequestId, 2, "decision-2"));
        var wrongSequence = await repository.CommitAsync(Proposal(request.DeliveryRequestId, 2, "decision-3"));

        Assert.Equal(DeliveryRequestWriteOutcome.Created, request.Outcome);
        Assert.Equal(PromiseCommitWriteOutcome.Committed, committed.Outcome);
        Assert.Equal(PromiseCommitWriteOutcome.Replayed, replayed.Outcome);
        Assert.Equal(committed.EventId, replayed.EventId);
        Assert.Equal(PromiseCommitWriteOutcome.Committed, revised.Outcome);
        Assert.Equal(PromiseCommitWriteOutcome.Conflict, wrongSequence.Outcome);

        await using var connection = await dataSource.OpenConnectionAsync();
        const string verifySql = """
            SELECT p.current_version,
                   (SELECT count(*) FROM promise_versions v WHERE v.promise_id = p.promise_id),
                   (SELECT count(*) FROM outbox_events e WHERE e.aggregate_id = p.promise_id),
                   (SELECT event_type FROM outbox_events e WHERE e.aggregate_id = p.promise_id ORDER BY sequence LIMIT 1),
                   (SELECT event_type FROM outbox_events e WHERE e.aggregate_id = p.promise_id ORDER BY sequence DESC LIMIT 1)
            FROM promises p
            WHERE p.customer_id = @customer_id AND p.delivery_request_id = @request_id;
            """;
        await using var verify = new NpgsqlCommand(verifySql, connection);
        verify.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customer.CustomerId);
        verify.Parameters.AddWithValue("request_id", NpgsqlDbType.Text, request.DeliveryRequestId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(2, reader.GetInt32(0));
        Assert.Equal(2L, reader.GetInt64(1));
        Assert.Equal(2L, reader.GetInt64(2));
        Assert.Equal("promise.committed", reader.GetString(3));
        Assert.Equal("promise.revised", reader.GetString(4));

        var otherTenantRepository = new PostgresPromiseCommitRepository(
            dataSource, new FixedCustomerContext(Customer.Create("Other", "ACTIVE").CustomerId));
        var hiddenRequest = await otherTenantRepository.CommitAsync(initial);
        Assert.Equal(PromiseCommitWriteOutcome.DeliveryRequestNotFound, hiddenRequest.Outcome);
    }

    private static PromiseCommitProposal Proposal(string requestId, int version, string decisionId) =>
        new(
            "prm_33333333333333333333333333333333",
            requestId,
            decisionId,
            version,
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            FeasibilityOutcome.Accepted,
            TimeSpan.FromMinutes(5),
            "buffer-v1",
            new Dictionary<string, string> { ["promise"] = "v1" },
            ["PROMISE_FEASIBLE"]);

    private sealed record FixedCustomerContext(string CustomerId) : ICustomerContext;

    private static async Task InsertCustomerAsync(NpgsqlDataSource dataSource, Customer customer)
    {
        const string sql = """
            INSERT INTO customers (customer_id, legal_name, status, created_at)
            VALUES (@customer_id, @legal_name, @status, @created_at);
            """;
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", customer.CustomerId);
        command.Parameters.AddWithValue("legal_name", customer.LegalName);
        command.Parameters.AddWithValue("status", customer.Status);
        command.Parameters.AddWithValue("created_at", customer.CreatedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertBranchAsync(NpgsqlDataSource dataSource, Branch branch)
    {
        const string sql = """
            INSERT INTO branches (branch_id, customer_id, name, status)
            VALUES (@branch_id, @customer_id, @name, @status);
            """;
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("branch_id", branch.BranchId);
        command.Parameters.AddWithValue("customer_id", branch.CustomerId);
        command.Parameters.AddWithValue("name", branch.Name);
        command.Parameters.AddWithValue("status", branch.Status);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ApplyMigrationsAsync(NpgsqlDataSource dataSource)
    {
        var assembly = typeof(PostgresPromiseCommitRepositoryTests).Assembly;
        foreach (var resourceName in new[]
                 {
                     "Mono.Api.Tests.Migrations.001_customer_branch.sql",
                     "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
                     "Mono.Api.Tests.Migrations.018_promise_commit.sql"
                 })
        {
            await using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("Missing embedded migration resource: " + resourceName);
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync();
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
