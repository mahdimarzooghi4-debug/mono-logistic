using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class PostgresDeliveryRequestRepositoryTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Idempotency_is_customer_scoped_and_replays_only_identical_payloads()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);

        var customer = Customer.Create("Mono Delivery Request Test", "ACTIVE");
        var otherCustomer = Customer.Create("Mono Other Delivery Request Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        await InsertCustomerAsync(dataSource, otherCustomer);

        var branch = Branch.Create(customer.CustomerId, "Delivery Test Branch", "ACTIVE");
        var otherBranch = Branch.Create(otherCustomer.CustomerId, "Other Delivery Test Branch", "ACTIVE");
        var ownerBranches = new CustomerBranchRepository(dataSource, new FixedCustomerContext(customer.CustomerId));
        var otherBranches = new CustomerBranchRepository(dataSource, new FixedCustomerContext(otherCustomer.CustomerId));
        await ownerBranches.CreateBranchAsync(branch);
        await otherBranches.CreateBranchAsync(otherBranch);

        var payload = new DeliveryRequestInput
        {
            ExternalReference = " ORDER-1 ",
            BranchId = branch.BranchId,
            Pickup = new DeliveryLocationInput { AddressText = "Store" },
            Dropoff = new DeliveryLocationInput { AddressText = "Customer" },
            Shipment = new DeliveryShipmentInput { ShipmentModel = "BAG_BASED", BagCount = 1, LoadClass = "S" },
            RequestedService = "URBAN_DELIVERY",
            RequestedPriority = "STANDARD"
        }.Normalize().ToCanonicalJson();
        var repository = new PostgresDeliveryRequestRepository(dataSource, new FixedCustomerContext(customer.CustomerId));

        var first = await repository.CreateOrReplayAsync(branch.BranchId, "ORDER-1", "retry-1", payload);
        var replay = await repository.CreateOrReplayAsync(branch.BranchId, "ORDER-1", "retry-1", payload);
        var statusForOwner = await repository.FindStatusAsync(first.DeliveryRequestId);
        var changed = await repository.CreateOrReplayAsync(
            branch.BranchId, "ORDER-1", "retry-1", payload.Replace("ORDER-1", "ORDER-2", StringComparison.Ordinal));
        var otherTenantPayload = new DeliveryRequestInput
        {
            ExternalReference = "ORDER-1",
            BranchId = otherBranch.BranchId,
            Pickup = new DeliveryLocationInput { AddressText = "Store" },
            Dropoff = new DeliveryLocationInput { AddressText = "Customer" },
            Shipment = new DeliveryShipmentInput { ShipmentModel = "BAG_BASED", BagCount = 1, LoadClass = "S" },
            RequestedService = "URBAN_DELIVERY",
            RequestedPriority = "STANDARD"
        }.ToCanonicalJson();
        var otherTenantRepository = new PostgresDeliveryRequestRepository(
            dataSource, new FixedCustomerContext(otherCustomer.CustomerId));
        var otherTenant = await otherTenantRepository.CreateOrReplayAsync(
            otherBranch.BranchId, "ORDER-1", "retry-1", otherTenantPayload);
        var statusForOtherCustomer = await otherTenantRepository.FindStatusAsync(first.DeliveryRequestId);

        Assert.Equal(DeliveryRequestWriteOutcome.Created, first.Outcome);
        Assert.Equal("RECEIVED", first.Status);
        Assert.NotNull(statusForOwner);
        Assert.Equal(first.DeliveryRequestId, statusForOwner.DeliveryRequestId);
        Assert.Equal("ORDER-1", statusForOwner.ExternalReference);
        Assert.Equal("RECEIVED", statusForOwner.Status);
        Assert.Null(statusForOtherCustomer);
        Assert.Equal(DeliveryRequestWriteOutcome.Replayed, replay.Outcome);
        Assert.Equal(first.DeliveryRequestId, replay.DeliveryRequestId);
        Assert.Equal(DeliveryRequestWriteOutcome.Conflict, changed.Outcome);
        Assert.Equal(first.DeliveryRequestId, changed.DeliveryRequestId);
        Assert.Equal(DeliveryRequestWriteOutcome.Created, otherTenant.Outcome);
        Assert.NotEqual(first.DeliveryRequestId, otherTenant.DeliveryRequestId);
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

    private static async Task ApplyMigrationsAsync(NpgsqlDataSource dataSource)
    {
        var assembly = typeof(PostgresDeliveryRequestRepositoryTests).Assembly;
        foreach (var resourceName in new[]
                 {
                     "Mono.Api.Tests.Migrations.001_customer_branch.sql",
                     "Mono.Api.Tests.Migrations.002_customer_api_keys.sql",
                     "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql"
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
