using Mono.Api.Domain;
using Mono.Api.Persistence;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

public sealed class CustomerBranchRepositoryTests(ITestOutputHelper output)
{
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
        await ApplyInitialMigrationAsync(dataSource);

        var repository = new CustomerBranchRepository(dataSource);
        var customer = Customer.Create("Mono Integration Test", "ACTIVE");
        var branch = Branch.Create(
            customer.CustomerId,
            "Test Branch",
            "ACTIVE",
            address: "Integration test address",
            latitude: 35.7,
            longitude: 51.4);

        await repository.CreateCustomerAsync(customer);
        await repository.CreateBranchAsync(branch);

        var foundForOwner = await repository.FindBranchAsync(customer.CustomerId, branch.BranchId);
        Assert.NotNull(foundForOwner);
        Assert.Equal(customer.CustomerId, foundForOwner.CustomerId);
        Assert.Equal(branch.BranchId, foundForOwner.BranchId);
        Assert.Equal(35.7, foundForOwner.Latitude);
        Assert.Null(await repository.FindBranchAsync("cus_00000000000000000000000000000000", branch.BranchId));

        var orphan = Branch.Create(
            "cus_00000000000000000000000000000000",
            "Orphan Branch",
            "ACTIVE");
        await Assert.ThrowsAsync<PostgresException>(() => repository.CreateBranchAsync(orphan));
    }

    private static async Task ApplyInitialMigrationAsync(NpgsqlDataSource dataSource)
    {
        const string resourceName = "Mono.Api.Tests.Migrations.001_customer_branch.sql";
        await using var stream = typeof(CustomerBranchRepositoryTests).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded migration resource: {resourceName}");
        using var reader = new StreamReader(stream);
        var migrationSql = await reader.ReadToEndAsync();

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(migrationSql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
