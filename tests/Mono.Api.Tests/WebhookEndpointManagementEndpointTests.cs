using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class WebhookEndpointManagementEndpointTests
{
    private const string ApiKey =
        "mono_live_0123456789abcdef_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Webhook_endpoint_management_is_tenant_scoped_audited_and_never_reveals_secrets_on_reads()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var owner = Customer.Create("Webhook Management Owner", "ACTIVE");
        var other = Customer.Create("Webhook Management Other", "ACTIVE");
        await InsertCustomerAsync(dataSource, owner);
        await InsertCustomerAsync(dataSource, other);

        var encryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var ownerFactory = CreateFactory(dataSource, owner.CustomerId, encryptionKey);
        await using var otherFactory = CreateFactory(dataSource, other.CustomerId, encryptionKey);
        using var ownerClient = ownerFactory.CreateClient();
        using var otherClient = otherFactory.CreateClient();
        ownerClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);
        otherClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);

        using var createdResponse = await ownerClient.PostAsJsonAsync("/v1/webhook-endpoints", new
        {
            url = "https://hooks.customer.com/mono",
            events = new[] { "mission.delivered" }
        });
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        var createdJson = await createdResponse.Content.ReadAsStringAsync();
        using var createdDocument = JsonDocument.Parse(createdJson);
        var endpointId = createdDocument.RootElement.GetProperty("endpoint_id").GetString()!;
        var originalSecret = createdDocument.RootElement.GetProperty("signing_secret").GetString()!;
        var path = "/v1/webhook-endpoints/" + endpointId;

        using var listResponse = await ownerClient.GetAsync("/v1/webhook-endpoints?limit=1");
        var listJson = await listResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Contains(endpointId, listJson, StringComparison.Ordinal);
        Assert.DoesNotContain(originalSecret, listJson, StringComparison.Ordinal);
        Assert.DoesNotContain("secret_ciphertext", listJson, StringComparison.OrdinalIgnoreCase);

        using var detailResponse = await ownerClient.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        Assert.DoesNotContain(originalSecret, await detailResponse.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync(path)).StatusCode);

        using var rotationResponse = await ownerClient.PostAsync(path + "/rotate-secret", content: null);
        var rotationJson = await rotationResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, rotationResponse.StatusCode);
        using var rotationDocument = JsonDocument.Parse(rotationJson);
        var rotatedSecret = rotationDocument.RootElement.GetProperty("signing_secret").GetString()!;
        Assert.NotEqual(originalSecret, rotatedSecret);
        Assert.DoesNotContain("secret_ciphertext", rotationJson, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.OK, (await ownerClient.PostAsync(path + "/disable", content: null)).StatusCode);
        using var repeatedDisable = await ownerClient.PostAsync(path + "/disable", content: null);
        Assert.Equal(HttpStatusCode.OK, repeatedDisable.StatusCode);
        Assert.Contains("\"status\":\"DISABLED\"", await repeatedDisable.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict,
            (await ownerClient.PostAsync(path + "/rotate-secret", content: null)).StatusCode);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var audit = new NpgsqlCommand("""
            SELECT array_agg(action ORDER BY event_id)
            FROM webhook_endpoint_events
            WHERE customer_id = @customer_id AND endpoint_id = @endpoint_id;
            """, connection);
        audit.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, owner.CustomerId);
        audit.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
        Assert.Equal(new[] { "CREATED", "SECRET_ROTATED", "DISABLED" },
            await audit.ExecuteScalarAsync() as string[]);

        await using var mutation = new NpgsqlCommand("""
            UPDATE webhook_endpoint_events SET action = 'CREATED'
            WHERE customer_id = @customer_id AND endpoint_id = @endpoint_id;
            """, connection);
        mutation.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, owner.CustomerId);
        mutation.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
        await Assert.ThrowsAsync<PostgresException>(() => mutation.ExecuteNonQueryAsync());
    }

    private static WebApplicationFactory<Program> CreateFactory(
        NpgsqlDataSource dataSource,
        string customerId,
        string encryptionKey) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?> { ["Mono:WebhookEncryptionKey"] = encryptionKey }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<NpgsqlDataSource>();
                services.AddSingleton(dataSource);
                services.RemoveAll<IApiKeyCredentialStore>();
                services.AddSingleton<IApiKeyCredentialStore>(new FixedCredentialStore(
                    new ApiKeyCredential(
                        "key_0123456789abcdef",
                        customerId,
                        ApiKeyCredentialVerifier.ComputeHash(ApiKey),
                        DateTimeOffset.UtcNow.AddHours(1),
                        null,
                        true,
                        ["webhooks.manage"])));
            });
        });

    private sealed class FixedCredentialStore(ApiKeyCredential credential) : IApiKeyCredentialStore
    {
        public Task<ApiKeyCredential?> FindAsync(string keyId, CancellationToken cancellationToken) =>
            Task.FromResult<ApiKeyCredential?>(keyId == credential.KeyId ? credential : null);
    }

    private static async Task InsertCustomerAsync(NpgsqlDataSource dataSource, Customer customer)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO customers (customer_id, legal_name, status, created_at)
            VALUES (@customer_id, @legal_name, @status, @created_at);
            """, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customer.CustomerId);
        command.Parameters.AddWithValue("legal_name", NpgsqlDbType.Text, customer.LegalName);
        command.Parameters.AddWithValue("status", NpgsqlDbType.Text, customer.Status);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, customer.CreatedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ApplyMigrationsAsync(NpgsqlDataSource dataSource)
    {
        var assembly = typeof(WebhookEndpointManagementEndpointTests).Assembly;
        foreach (var resourceName in new[]
                 {
                     "Mono.Api.Tests.Migrations.001_customer_branch.sql",
                     "Mono.Api.Tests.Migrations.002_customer_api_keys.sql",
                     "Mono.Api.Tests.Migrations.054_webhook_endpoint_registry.sql",
                     "Mono.Api.Tests.Migrations.055_webhook_endpoint_lifecycle_audit.sql"
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
