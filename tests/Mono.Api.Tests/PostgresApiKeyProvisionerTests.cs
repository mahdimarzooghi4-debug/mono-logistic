using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class PostgresApiKeyProvisionerTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Provisioner_stores_hash_and_authentication_can_read_the_credential()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var customer = Customer.Create("Mono API Key Provisioner Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        var now = DateTimeOffset.UtcNow;
        var issued = MonoApiKeyIssuer.Create(customer.CustomerId, ["branches.read"], now.AddDays(30), now);

        await new PostgresApiKeyProvisioner(dataSource).CreateAsync(issued.Credential);

        var stored = await new PostgresApiKeyCredentialStore(dataSource)
            .FindAsync(issued.Credential.KeyId, CancellationToken.None);
        Assert.NotNull(stored);
        Assert.Equal(issued.Credential.SecretHash, stored.SecretHash);
        Assert.NotEqual(issued.Value, stored.SecretHash);
        Assert.Equal(new[] { "branches.read" }, stored.Scopes);
        Assert.True(ApiKeyCredentialVerifier.TryValidate(issued.Value, stored, now, out _));
    }

    [Fact]
    public async Task Provisioner_rejects_inactive_customer_without_storing_a_credential()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            output.WriteLine("Skipped database integration coverage: MONO_TEST_DATABASE is not configured.");
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var customer = Customer.Create("Mono Inactive API Key Test", "INACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        var now = DateTimeOffset.UtcNow;
        var issued = MonoApiKeyIssuer.Create(customer.CustomerId, ["branches.read"], now.AddDays(30), now);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PostgresApiKeyProvisioner(dataSource).CreateAsync(issued.Credential));

        var stored = await new PostgresApiKeyCredentialStore(dataSource)
            .FindAsync(issued.Credential.KeyId, CancellationToken.None);
        Assert.Null(stored);
    }

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
        var assembly = typeof(PostgresApiKeyProvisionerTests).Assembly;
        foreach (var resourceName in new[]
                 {
                     "Mono.Api.Tests.Migrations.001_customer_branch.sql",
                     "Mono.Api.Tests.Migrations.002_customer_api_keys.sql",
                     "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
                     "Mono.Api.Tests.Migrations.004_delivery_request_list_index.sql",
            "Mono.Api.Tests.Migrations.005_customer_service_configurations.sql"
                 })
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
