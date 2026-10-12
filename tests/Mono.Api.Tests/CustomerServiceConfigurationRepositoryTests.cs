using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class CustomerServiceConfigurationRepositoryTests
{
    [Fact]
    public async Task Effective_configuration_is_customer_scoped_versioned_and_effective_dated()
    {
        var dataSource = await CreateDataSourceAsync();
        if (dataSource is null)
            return;

        await using (dataSource)
        {
            var ownerId = await InsertCustomerAsync(dataSource, "Mono Service Config Owner");
            var otherId = await InsertCustomerAsync(dataSource, "Mono Service Config Other");
            var evaluatedAt = DateTimeOffset.UtcNow;
            await InsertConfigurationAsync(dataSource, ownerId, "URBAN_DELIVERY", 1, "ACTIVE",
                evaluatedAt.AddHours(-1), evaluatedAt.AddHours(1), "{\"enabled\":true}");
            await InsertConfigurationAsync(dataSource, ownerId, "URBAN_DELIVERY", 2, "DRAFT",
                evaluatedAt.AddHours(-1), null, "{\"enabled\":false}");
            await InsertConfigurationAsync(dataSource, otherId, "INTERCITY_DELIVERY", 1, "ACTIVE",
                evaluatedAt.AddHours(-1), null, "{\"enabled\":true}");

            var ownerRepository = new CustomerServiceConfigurationRepository(dataSource, new FixedCustomerContext(ownerId));
            var configuration = await ownerRepository.FindEffectiveAsync("urban_delivery", evaluatedAt);
            var foreignConfiguration = await ownerRepository.FindEffectiveAsync("INTERCITY_DELIVERY", evaluatedAt);

            Assert.NotNull(configuration);
            Assert.Equal(ownerId, configuration.CustomerId);
            Assert.Equal("URBAN_DELIVERY", configuration.ServiceCode);
            Assert.Equal(1, configuration.Version);
            Assert.Equal("ACTIVE", configuration.Status);
            Assert.True(configuration.Configuration.GetProperty("enabled").GetBoolean());
            Assert.Null(foreignConfiguration);

            var otherRepository = new CustomerServiceConfigurationRepository(dataSource, new FixedCustomerContext(otherId));
            Assert.NotNull(await otherRepository.FindEffectiveAsync("INTERCITY_DELIVERY", evaluatedAt));
        }
    }

    [Fact]
    public async Task Missing_or_out_of_window_configuration_returns_no_effective_configuration()
    {
        var dataSource = await CreateDataSourceAsync();
        if (dataSource is null)
            return;

        await using (dataSource)
        {
            var customerId = await InsertCustomerAsync(dataSource, "Mono Service Config Window");
            var evaluatedAt = DateTimeOffset.UtcNow;
            await InsertConfigurationAsync(dataSource, customerId, "URBAN_DELIVERY", 1, "ACTIVE",
                evaluatedAt.AddHours(-2), evaluatedAt.AddHours(-1), "{\"enabled\":true}");

            var repository = new CustomerServiceConfigurationRepository(dataSource, new FixedCustomerContext(customerId));
            Assert.Null(await repository.FindEffectiveAsync("URBAN_DELIVERY", evaluatedAt));
            Assert.Null(await repository.FindEffectiveAsync("UNCONFIGURED", evaluatedAt));
        }
    }

    [Fact]
    public async Task Overlapping_active_versions_fail_closed()
    {
        var dataSource = await CreateDataSourceAsync();
        if (dataSource is null)
            return;

        await using (dataSource)
        {
            var customerId = await InsertCustomerAsync(dataSource, "Mono Service Config Conflict");
            var evaluatedAt = DateTimeOffset.UtcNow;
            await InsertConfigurationAsync(dataSource, customerId, "URBAN_DELIVERY", 1, "ACTIVE",
                evaluatedAt.AddHours(-1), evaluatedAt.AddHours(1), "{\"version\":1}");
            await InsertConfigurationAsync(dataSource, customerId, "URBAN_DELIVERY", 2, "ACTIVE",
                evaluatedAt.AddMinutes(-30), evaluatedAt.AddHours(2), "{\"version\":2}");

            var repository = new CustomerServiceConfigurationRepository(dataSource, new FixedCustomerContext(customerId));
            await Assert.ThrowsAsync<AmbiguousServiceConfigurationException>(
                () => repository.FindEffectiveAsync("URBAN_DELIVERY", evaluatedAt));
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("urban delivery")]
    [InlineData("urban-delivery")]
    public async Task Invalid_service_codes_are_rejected_before_query(string serviceCode)
    {
        await using var dataSource = NpgsqlDataSource.Create("Host=localhost;Username=unused;Password=unused;Database=unused");
        var repository = new CustomerServiceConfigurationRepository(dataSource, new FixedCustomerContext("cus_unused"));
        await Assert.ThrowsAsync<ArgumentException>(
            () => repository.FindEffectiveAsync(serviceCode, DateTimeOffset.UtcNow));
    }

    private static async Task<NpgsqlDataSource?> CreateDataSourceAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        return dataSource;
    }

    private static async Task<string> InsertCustomerAsync(NpgsqlDataSource dataSource, string name)
    {
        var customerId = "cus_" + Guid.NewGuid().ToString("N");
        const string sql = """
            INSERT INTO customers (customer_id, legal_name, status, created_at)
            VALUES (@customer_id, @legal_name, 'ACTIVE', @created_at);
            """;
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("legal_name", NpgsqlDbType.Text, name);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
        return customerId;
    }

    private static async Task InsertConfigurationAsync(
        NpgsqlDataSource dataSource,
        string customerId,
        string serviceCode,
        int version,
        string status,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo,
        string configurationJson)
    {
        const string sql = """
            INSERT INTO customer_service_configurations (
                service_configuration_id, customer_id, service_code, version, status,
                effective_from, effective_to, configuration, created_at)
            VALUES (
                @service_configuration_id, @customer_id, @service_code, @version, @status,
                @effective_from, @effective_to, @configuration, @created_at);
            """;
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("service_configuration_id", NpgsqlDbType.Text, "scf_" + Guid.NewGuid().ToString("N"));
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("service_code", NpgsqlDbType.Text, serviceCode);
        command.Parameters.AddWithValue("version", NpgsqlDbType.Integer, version);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, status);
        command.Parameters.AddWithValue("effective_from", NpgsqlDbType.TimestampTz, effectiveFrom);
        command.Parameters.AddWithValue("effective_to", NpgsqlDbType.TimestampTz, effectiveTo ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("configuration", NpgsqlDbType.Jsonb, configurationJson);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record FixedCustomerContext(string CustomerId) : ICustomerContext;

    private static async Task ApplyMigrationsAsync(NpgsqlDataSource dataSource)
    {
        var assembly = typeof(CustomerServiceConfigurationRepositoryTests).Assembly;
        var migrationNames = new[]
        {
            "Mono.Api.Tests.Migrations.001_customer_branch.sql",
            "Mono.Api.Tests.Migrations.002_customer_api_keys.sql",
            "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
            "Mono.Api.Tests.Migrations.004_delivery_request_list_index.sql",
            "Mono.Api.Tests.Migrations.005_customer_service_configurations.sql"
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
