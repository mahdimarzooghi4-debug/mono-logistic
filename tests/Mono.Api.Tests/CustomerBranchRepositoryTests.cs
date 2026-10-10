using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class CustomerBranchRepositoryTests(ITestOutputHelper output)
{
    [Fact]
    public void Branch_rejects_invalid_or_partial_coordinates()
    {
        Assert.Throws<ArgumentException>(() =>
            Branch.Create("cus_00000000000000000000000000000000", "Test", "ACTIVE", latitude: 35.7));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Branch.Create("cus_00000000000000000000000000000000", "Test", "ACTIVE", latitude: 91, longitude: 51));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Branch.Create("cus_00000000000000000000000000000000", "Test", "ACTIVE", latitude: 35, longitude: 181));
    }

    [Fact]
    public async Task Branch_listing_is_customer_scoped_and_stably_ordered()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);

        var customer = Customer.Create("Mono Branch List Test", "ACTIVE");
        var otherCustomer = Customer.Create("Mono Other Branch List Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        await InsertCustomerAsync(dataSource, otherCustomer);

        var repository = new CustomerBranchRepository(dataSource, new FixedCustomerContext(customer.CustomerId));
        var zulu = Branch.Create(customer.CustomerId, "Zulu", "ACTIVE", address: "Z address");
        var alpha = Branch.Create(customer.CustomerId, "Alpha", "ACTIVE", address: "A address");
        var foreign = Branch.Create(otherCustomer.CustomerId, "Foreign", "ACTIVE");
        await repository.CreateBranchAsync(zulu);
        await repository.CreateBranchAsync(alpha);
        var otherRepository = new CustomerBranchRepository(dataSource, new FixedCustomerContext(otherCustomer.CustomerId));
        await otherRepository.CreateBranchAsync(foreign);

        var branches = await repository.ListBranchesAsync();

        Assert.Equal(new[] { alpha.BranchId, zulu.BranchId }, branches.Select(branch => branch.BranchId));
        Assert.All(branches, branch => Assert.Equal(customer.CustomerId, branch.CustomerId));
    }

    [Fact]
    public async Task Branch_lookup_is_scoped_to_its_customer_and_foreign_key_is_enforced()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);

        var customer = Customer.Create("Mono Integration Test", "ACTIVE");
        var repository = new CustomerBranchRepository(dataSource, new FixedCustomerContext(customer.CustomerId));
        var branch = Branch.Create(
            customer.CustomerId,
            "Test Branch",
            "ACTIVE",
            address: "Integration test address",
            latitude: 35.7,
            longitude: 51.4);

        await InsertCustomerAsync(dataSource, customer);
        await repository.CreateBranchAsync(branch);

        var foundForOwner = await repository.FindBranchAsync(branch.BranchId);
        Assert.NotNull(foundForOwner);
        Assert.Equal(customer.CustomerId, foundForOwner.CustomerId);
        Assert.Equal(branch.BranchId, foundForOwner.BranchId);
        Assert.Equal(35.7, foundForOwner.Latitude);
        var otherTenantId = "cus_00000000000000000000000000000000";
        var otherTenantRepository = new CustomerBranchRepository(dataSource, new FixedCustomerContext(otherTenantId));
        Assert.Null(await otherTenantRepository.FindBranchAsync(branch.BranchId));

        var wrongTenantBranch = Branch.Create(otherTenantId, "Wrong Tenant", "ACTIVE");
        await Assert.ThrowsAsync<CustomerContextUnavailableException>(() => repository.CreateBranchAsync(wrongTenantBranch));

        var orphan = Branch.Create(otherTenantId, "Orphan Branch", "ACTIVE");
        await Assert.ThrowsAsync<PostgresException>(() => otherTenantRepository.CreateBranchAsync(orphan));
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
        var assembly = typeof(CustomerBranchRepositoryTests).Assembly;
        var migrationNames = new[]
        {
            "Mono.Api.Tests.Migrations.001_customer_branch.sql",
            "Mono.Api.Tests.Migrations.002_customer_api_keys.sql",
            "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
                     "Mono.Api.Tests.Migrations.004_delivery_request_list_index.sql"
        };

        foreach (var resourceName in migrationNames)
        {
            await using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException("Missing embedded migration resource: " + resourceName);
            using var reader = new StreamReader(stream);
            var migrationSql = await reader.ReadToEndAsync();

            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(migrationSql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
