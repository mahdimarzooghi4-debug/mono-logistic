using System.Text.Json;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class CustomerServiceConfigurationInspectionTests
{
    [Fact]
    public async Task Admin_can_inspect_versions_and_audit_without_exposing_configuration_payload()
    {
        var dataSource = await CreateDataSourceAsync();
        if (dataSource is null) return;
        await using (dataSource)
        {
            var customerId = "cus_" + Guid.NewGuid().ToString("N");
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO customers (customer_id, legal_name, status, created_at)
                VALUES (@id, 'Mono Inspection Test', 'ACTIVE', @at);
                """, connection))
            {
                insert.Parameters.AddWithValue("id", NpgsqlDbType.Text, customerId);
                insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow);
                await insert.ExecuteNonQueryAsync();
            }

            var actor = new FixedOperationsContext("operator-a", [MonoOperationsRoles.Administrator]);
            var authoring = new CustomerServiceConfigurationAuthoringService(dataSource, actor);
            using var config = JsonDocument.Parse("{\"internalValue\":\"do-not-return\"}");
            var draft = await authoring.CreateDraftAsync(customerId, "URBAN_DELIVERY",
                DateTimeOffset.UtcNow, null, config.RootElement, "initial configuration");
            await authoring.ActivateDraftAsync(customerId, draft.ServiceConfigurationId, "operator approval");
            var nextDraft = await authoring.CreateDraftAsync(customerId, "URBAN_DELIVERY",
                DateTimeOffset.UtcNow.AddDays(1), null, config.RootElement, "future version");

            var inspection = new CustomerServiceConfigurationInspectionService(dataSource, actor);
            var versions = await inspection.ListVersionsAsync(customerId, "urban_delivery");
            var audit = await inspection.ListAuditHistoryAsync(customerId, "URBAN_DELIVERY");

            Assert.Equal(new[] { nextDraft.ServiceConfigurationId, draft.ServiceConfigurationId },
                versions.Select(item => item.ServiceConfigurationId));
            Assert.Equal(new[] { 2, 1 }, versions.Select(item => item.Version));
            Assert.Equal(new[] { "DRAFT_CREATED", "ACTIVATED", "DRAFT_CREATED" },
                audit.OrderBy(item => item.OccurredAt).Select(item => item.Action));
            Assert.All(audit, item => Assert.Equal("operator-a", item.ActorId));
            Assert.DoesNotContain("internalValue", string.Join("|", versions.Select(x => x.ToString())));
        }
    }

    [Fact]
    public async Task Non_admin_cannot_inspect_and_invalid_limits_are_rejected()
    {
        await using var dataSource = NpgsqlDataSource.Create("Host=localhost;Username=unused;Password=unused;Database=unused");
        var service = new CustomerServiceConfigurationInspectionService(dataSource,
            new FixedOperationsContext("operator-b", ["MONO_OPERATOR"]));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ListVersionsAsync("cus_00000000000000000000000000000000", "URBAN_DELIVERY"));
        var adminService = new CustomerServiceConfigurationInspectionService(dataSource,
            new FixedOperationsContext("operator-c", [MonoOperationsRoles.Administrator]));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            adminService.ListAuditHistoryAsync("cus_00000000000000000000000000000000", "URBAN_DELIVERY", 101));
    }

    private sealed record FixedOperationsContext(string ActorId, IReadOnlyCollection<string> Roles)
        : IMonoOperationsContext;

    private static async Task<NpgsqlDataSource?> CreateDataSourceAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        var dataSource = NpgsqlDataSource.Create(connectionString);
        var assembly = typeof(CustomerServiceConfigurationInspectionTests).Assembly;
        foreach (var name in new[]
        {
            "Mono.Api.Tests.Migrations.001_customer_branch.sql",
            "Mono.Api.Tests.Migrations.002_customer_api_keys.sql",
            "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
            "Mono.Api.Tests.Migrations.004_delivery_request_list_index.sql",
            "Mono.Api.Tests.Migrations.005_customer_service_configurations.sql",
            "Mono.Api.Tests.Migrations.006_customer_service_configuration_audit.sql"
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
}
