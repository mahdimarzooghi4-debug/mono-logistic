using System.Text.Json;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class CustomerServiceConfigurationAuthoringTests
{
    [Fact]
    public async Task Draft_version_activation_and_overlap_are_audited()
    {
        var dataSource = await CreateDataSourceAsync();
        if (dataSource is null) return;
        await using (dataSource)
        {
            var customerId = await InsertCustomerAsync(dataSource);
            var service = new CustomerServiceConfigurationAuthoringService(dataSource,
                new FixedOperationsContext("operator-1", [MonoOperationsRoles.Administrator]));
            using var config = JsonDocument.Parse("{\"modes\":[\"WALK\"]}");
            var start = DateTimeOffset.UtcNow.AddMinutes(-1);
            var first = await service.CreateDraftAsync(customerId, "urban_delivery", start, null,
                config.RootElement, "initial setup");
            Assert.Equal(1, first.Version);
            Assert.Equal("DRAFT", first.Status);
            await service.ActivateDraftAsync(customerId, first.ServiceConfigurationId, "approved by operations");

            var overlapping = await service.CreateDraftAsync(customerId, "URBAN_DELIVERY",
                start.AddMinutes(10), null, config.RootElement, "new version");
            Assert.Equal(2, overlapping.Version);
            await Assert.ThrowsAsync<ConfigurationActivationConflictException>(() =>
                service.ActivateDraftAsync(customerId, overlapping.ServiceConfigurationId, "overlap attempt"));

            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand("""
                SELECT action, actor_id, reason FROM customer_service_configuration_audit
                WHERE customer_id = @customer_id ORDER BY occurred_at, audit_id;
                """, connection);
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            await using var reader = await command.ExecuteReaderAsync();
            var audit = new List<(string Action, string Actor, string Reason)>();
            while (await reader.ReadAsync())
                audit.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            Assert.Equal(new[] { "DRAFT_CREATED", "ACTIVATED", "DRAFT_CREATED" }, audit.Select(x => x.Action));
            Assert.All(audit, x => Assert.Equal("operator-1", x.Actor));
            Assert.Equal("initial setup", audit[0].Reason);
            Assert.Equal("approved by operations", audit[1].Reason);
        }
    }

    [Fact]
    public async Task Active_configuration_can_be_retired_once_and_audit_is_appended()
    {
        var dataSource = await CreateDataSourceAsync();
        if (dataSource is null) return;
        await using (dataSource)
        {
            var customerId = await InsertCustomerAsync(dataSource);
            var service = new CustomerServiceConfigurationAuthoringService(dataSource,
                new FixedOperationsContext("operator-retire", [MonoOperationsRoles.Administrator]));
            using var config = JsonDocument.Parse("{\"modes\":[\"WALK\"]}");
            var draft = await service.CreateDraftAsync(customerId, "URBAN_DELIVERY",
                DateTimeOffset.UtcNow, null, config.RootElement, "retirement setup");
            await service.ActivateDraftAsync(customerId, draft.ServiceConfigurationId, "activate for retirement test");

            await service.RetireActiveAsync(customerId, draft.ServiceConfigurationId, "service discontinued");
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.RetireActiveAsync(customerId, draft.ServiceConfigurationId, "duplicate retirement"));

            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand("""
                SELECT c.status, a.action, a.actor_id, a.reason
                FROM customer_service_configurations c
                JOIN customer_service_configuration_audit a
                  ON a.service_configuration_id = c.service_configuration_id
                WHERE c.service_configuration_id = @configuration_id
                ORDER BY a.occurred_at, a.audit_id;
                """, connection);
            command.Parameters.AddWithValue("configuration_id", NpgsqlDbType.Text, draft.ServiceConfigurationId);
            await using var reader = await command.ExecuteReaderAsync();
            var audit = new List<(string Status, string Action, string Actor, string Reason)>();
            while (await reader.ReadAsync())
                audit.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            Assert.Equal("RETIRED", audit[^1].Status);
            Assert.Equal(new[] { "DRAFT_CREATED", "ACTIVATED", "RETIRED" }, audit.Select(x => x.Action));
            Assert.All(audit, x => Assert.Equal("operator-retire", x.Actor));
            Assert.Equal("service discontinued", audit[^1].Reason);
        }
    }

    [Fact]
    public async Task Non_admin_context_fails_before_database_access()
    {
        await using var dataSource = NpgsqlDataSource.Create("Host=localhost;Username=unused;Password=unused;Database=unused");
        var service = new CustomerServiceConfigurationAuthoringService(dataSource,
            new FixedOperationsContext("operator-2", ["MONO_OPERATOR"]));
        using var config = JsonDocument.Parse("{}");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.CreateDraftAsync(
            "cus_00000000000000000000000000000000", "URBAN_DELIVERY",
            DateTimeOffset.UtcNow, null, config.RootElement, "test"));
    }

    [Fact]
    public async Task Audit_rows_reject_update()
    {
        var dataSource = await CreateDataSourceAsync();
        if (dataSource is null) return;
        await using (dataSource)
        {
            var customerId = await InsertCustomerAsync(dataSource);
            var service = new CustomerServiceConfigurationAuthoringService(dataSource,
                new FixedOperationsContext("operator-3", [MonoOperationsRoles.Administrator]));
            using var config = JsonDocument.Parse("{}");
            var draft = await service.CreateDraftAsync(customerId, "URBAN_DELIVERY",
                DateTimeOffset.UtcNow, null, config.RootElement, "immutability test");
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand("""
                UPDATE customer_service_configuration_audit SET reason='changed'
                WHERE service_configuration_id=@configuration_id;
                """, connection);
            command.Parameters.AddWithValue("configuration_id", NpgsqlDbType.Text, draft.ServiceConfigurationId);
            await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        }
    }

    private sealed record FixedOperationsContext(string ActorId, IReadOnlyCollection<string> Roles)
        : IMonoOperationsContext;

    private static async Task<NpgsqlDataSource?> CreateDataSourceAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        var dataSource = NpgsqlDataSource.Create(connectionString);
        var assembly = typeof(CustomerServiceConfigurationAuthoringTests).Assembly;
        foreach (var name in new[]
        {
            "Mono.Api.Tests.Migrations.001_customer_branch.sql",
            "Mono.Api.Tests.Migrations.002_customer_api_keys.sql",
            "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
            "Mono.Api.Tests.Migrations.004_delivery_request_list_index.sql",
            "Mono.Api.Tests.Migrations.005_customer_service_configurations.sql",
            "Mono.Api.Tests.Migrations.006_customer_service_configuration_audit.sql",
            "Mono.Api.Tests.Migrations.007_customer_service_configuration_retirement.sql"
        })
        {
            await using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("Missing embedded migration resource: " + name);
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync();
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        return dataSource;
    }

    private static async Task<string> InsertCustomerAsync(NpgsqlDataSource dataSource)
    {
        var id = "cus_" + Guid.NewGuid().ToString("N");
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO customers (customer_id, legal_name, status, created_at)
            VALUES (@id, 'Mono Config Authoring Test', 'ACTIVE', @at);
            """, connection);
        command.Parameters.AddWithValue("id", NpgsqlDbType.Text, id);
        command.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync();
        return id;
    }
}
