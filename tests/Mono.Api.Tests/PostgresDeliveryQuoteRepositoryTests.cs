using System.Collections.Immutable;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class PostgresDeliveryQuoteRepositoryTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Persists_next_version_replays_identical_snapshot_and_rejects_conflicts()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var customer = Customer.Create("Mono Quote Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        var branch = Branch.Create(customer.CustomerId, "Quote Test Branch", "ACTIVE");
        await InsertBranchAsync(dataSource, branch);
        var context = new FixedCustomerContext(customer.CustomerId);
        var intake = new PostgresDeliveryRequestRepository(dataSource, context);
        var payload = new DeliveryRequestInput
        {
            ExternalReference = "QUOTE-1",
            BranchId = branch.BranchId,
            Pickup = new DeliveryLocationInput { AddressText = "Store" },
            Dropoff = new DeliveryLocationInput { AddressText = "Customer" },
            Shipment = new DeliveryShipmentInput { ShipmentModel = "BAG_BASED", BagCount = 1, LoadClass = "S" },
            RequestedService = "URBAN_DELIVERY",
            RequestedPriority = "STANDARD"
        }.Normalize().ToCanonicalJson();
        var request = await intake.CreateOrReplayAsync(branch.BranchId, "QUOTE-1", "quote-intake-1", payload);
        var repository = new PostgresDeliveryQuoteRepository(dataSource, context);
        var first = Quote(customer.CustomerId, request.DeliveryRequestId, 1, 1_500m);
        var persisted = await repository.PersistAsync(first);
        var replayed = await repository.PersistAsync(first);
        var alteredReplay = await repository.PersistAsync(first with
        {
            AuditEvidenceIds = ImmutableArray.Create("different-evidence")
        });
        var skippedVersion = await repository.PersistAsync(Quote(
            customer.CustomerId, request.DeliveryRequestId, 3, 2_500m));
        var nextVersion = await repository.PersistAsync(Quote(
            customer.CustomerId, request.DeliveryRequestId, 2, 2_500m));
        var concurrentA = Quote(customer.CustomerId, request.DeliveryRequestId, 3, 3_500m) with
        {
            QuoteId = "quote_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        };
        var concurrentB = Quote(customer.CustomerId, request.DeliveryRequestId, 3, 4_500m) with
        {
            QuoteId = "quote_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
        };
        var concurrentResults = await Task.WhenAll(
            repository.PersistAsync(concurrentA),
            repository.PersistAsync(concurrentB));

        Assert.Equal(DeliveryQuoteWriteOutcome.Persisted, persisted.Outcome);
        Assert.Equal(DeliveryQuoteWriteOutcome.Replayed, replayed.Outcome);
        Assert.Equal(DeliveryQuoteWriteOutcome.Conflict, alteredReplay.Outcome);
        Assert.Equal(DeliveryQuoteWriteOutcome.Conflict, skippedVersion.Outcome);
        Assert.Equal(DeliveryQuoteWriteOutcome.Persisted, nextVersion.Outcome);
        Assert.Single(concurrentResults, result => result.Outcome == DeliveryQuoteWriteOutcome.Persisted);
        Assert.Single(concurrentResults, result => result.Outcome == DeliveryQuoteWriteOutcome.Conflict);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*), max(version)
            FROM delivery_quote_versions
            WHERE customer_id = @customer_id AND delivery_request_id = @request_id;
            """, connection);
        count.Parameters.AddWithValue("customer_id", customer.CustomerId);
        count.Parameters.AddWithValue("request_id", request.DeliveryRequestId);
        await using var reader = await count.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(3L, reader.GetInt64(0));
        Assert.Equal(3, reader.GetInt32(1));

        var otherTenantId = Customer.Create("Other Quote Tenant", "ACTIVE").CustomerId;
        var otherTenant = new PostgresDeliveryQuoteRepository(
            dataSource, new FixedCustomerContext(otherTenantId));
        var hiddenRequest = await otherTenant.PersistAsync(
            Quote(otherTenantId, request.DeliveryRequestId, 1, 1_500m));
        Assert.Equal(DeliveryQuoteWriteOutcome.DeliveryRequestNotFound, hiddenRequest.Outcome);
    }

    [Fact]
    public async Task Initial_quote_idempotency_is_atomic_and_binds_key_to_request_hash()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var customer = Customer.Create("Mono Quote Issuance Idempotency Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        var branch = Branch.Create(customer.CustomerId, "Quote Idempotency Branch", "ACTIVE");
        await InsertBranchAsync(dataSource, branch);
        var intake = new PostgresDeliveryRequestRepository(
            dataSource, new FixedCustomerContext(customer.CustomerId));
        var payload = new DeliveryRequestInput
        {
            ExternalReference = "QUOTE-IDEMPOTENCY-1",
            BranchId = branch.BranchId,
            Pickup = new DeliveryLocationInput { AddressText = "Store" },
            Dropoff = new DeliveryLocationInput { AddressText = "Customer" },
            Shipment = new DeliveryShipmentInput { ShipmentModel = "BAG_BASED", BagCount = 1, LoadClass = "S" },
            RequestedService = "URBAN_DELIVERY",
            RequestedPriority = "STANDARD"
        }.Normalize().ToCanonicalJson();
        var request = await intake.CreateOrReplayAsync(
            branch.BranchId, "QUOTE-IDEMPOTENCY-1", "quote-idem-intake", payload);
        var repository = new PostgresDeliveryQuoteRepository(
            dataSource, new FixedCustomerContext(customer.CustomerId));
        var first = Quote(customer.CustomerId, request.DeliveryRequestId, 1, 2_500m) with
        {
            QuoteId = "quote_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
        };
        var concurrentRetry = first with
        {
            QuoteId = "quote_bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
        };
        const string requestHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        var concurrent = await Task.WhenAll(
            repository.IssueInitialIdempotentlyAsync(first, "quote-create-1", requestHash),
            repository.IssueInitialIdempotentlyAsync(concurrentRetry, "quote-create-1", requestHash));
        var replay = await repository.IssueInitialIdempotentlyAsync(
            concurrentRetry, "quote-create-1", requestHash);
        var changedRequest = await repository.IssueInitialIdempotentlyAsync(
            concurrentRetry, "quote-create-1",
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var secondKey = await repository.IssueInitialIdempotentlyAsync(
            concurrentRetry, "quote-create-2", requestHash);

        Assert.Single(concurrent, result => result.Outcome == DeliveryQuoteWriteOutcome.Persisted);
        Assert.Single(concurrent, result => result.Outcome == DeliveryQuoteWriteOutcome.Replayed);
        Assert.Equal(DeliveryQuoteWriteOutcome.Replayed, replay.Outcome);
        Assert.Equal(first.QuoteId, replay.QuoteId);
        Assert.Equal(DeliveryQuoteWriteOutcome.IdempotencyConflict, changedRequest.Outcome);
        Assert.Equal(DeliveryQuoteWriteOutcome.Conflict, secondKey.Outcome);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*)
            FROM delivery_quote_versions
            WHERE customer_id = @customer_id AND delivery_request_id = @request_id;
            """, connection);
        count.Parameters.AddWithValue("customer_id", customer.CustomerId);
        count.Parameters.AddWithValue("request_id", request.DeliveryRequestId);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Rejects_quote_whose_customer_does_not_match_authenticated_tenant()
    {
        var repoCustomer = Customer.Create("Context", "ACTIVE").CustomerId;
        var otherCustomer = Customer.Create("Quote", "ACTIVE").CustomerId;
        var quote = Quote(otherCustomer, "drq_22222222222222222222222222222222", 1, 1_500m);
        await using var dataSource = NpgsqlDataSource.Create(
            Environment.GetEnvironmentVariable("MONO_TEST_DATABASE") ?? "Host=localhost;Database=unused");
        var repository = new PostgresDeliveryQuoteRepository(dataSource, new FixedCustomerContext(repoCustomer));

        await Assert.ThrowsAsync<ArgumentException>(() => repository.PersistAsync(quote));
    }

    private static DeliveryQuote Quote(string customerId, string requestId, int version, decimal distanceMeters)
    {
        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        return new DeliveryQuote(
            "quote_11111111111111111111111111111111",
            customerId,
            requestId,
            version,
            issuedAt,
            issuedAt.AddMinutes(15),
            HanaDistancePricingV1.Calculate(distanceMeters),
            ImmutableDictionary<string, string>.Empty.Add("pricing", HanaDistancePricingV1.PolicyVersion),
            ImmutableArray.Create("feasibility_11111111111111111111111111111111"));
    }

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
        var assembly = typeof(PostgresDeliveryQuoteRepositoryTests).Assembly;
        foreach (var resourceName in new[]
                 {
                     "Mono.Api.Tests.Migrations.001_customer_branch.sql",
                     "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
                     "Mono.Api.Tests.Migrations.018_promise_commit.sql",
                     "Mono.Api.Tests.Migrations.019_delivery_quote_versions.sql",
                     "Mono.Api.Tests.Migrations.020_delivery_quote_issuance_idempotency.sql"
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
