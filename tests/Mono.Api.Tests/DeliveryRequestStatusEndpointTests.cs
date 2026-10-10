using System.Net.Http.Json;
using Mono.Api.Domain;
using Mono.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class DeliveryRequestStatusEndpointTests
{
    private const string ApiKey =
        "mono_live_0123456789abcdef_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Status_endpoint_returns_only_customer_owned_request()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);

        var customer = Customer.Create("Mono Status Endpoint Test", "ACTIVE");
        var otherCustomer = Customer.Create("Mono Other Status Endpoint Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        await InsertCustomerAsync(dataSource, otherCustomer);
        var branch = Branch.Create(customer.CustomerId, "Status Endpoint Branch", "ACTIVE");
        var otherBranch = Branch.Create(otherCustomer.CustomerId, "Other Status Endpoint Branch", "ACTIVE");
        var ownerBranches = new CustomerBranchRepository(dataSource, new FixedCustomerContext(customer.CustomerId));
        var otherBranches = new CustomerBranchRepository(dataSource, new FixedCustomerContext(otherCustomer.CustomerId));
        await ownerBranches.CreateBranchAsync(branch);
        await otherBranches.CreateBranchAsync(otherBranch);
        var request = new DeliveryRequestInput
        {
            ExternalReference = "ORDER-STATUS-1",
            BranchId = branch.BranchId,
            Pickup = new DeliveryLocationInput { AddressText = "Store" },
            Dropoff = new DeliveryLocationInput { AddressText = "Customer" },
            Shipment = new DeliveryShipmentInput { ShipmentModel = "BAG_BASED", BagCount = 1, LoadClass = "S" },
            RequestedService = "URBAN_DELIVERY",
            RequestedPriority = "STANDARD"
        };
        var repo = new Mono.Api.Persistence.PostgresDeliveryRequestRepository(
            dataSource, new FixedCustomerContext(customer.CustomerId));
        var created = await repo.CreateOrReplayAsync(
            branch.BranchId, request.ExternalReference, "status-endpoint", request.ToCanonicalJson());

        using var ownerClient = CreateClient(dataSource, customer.CustomerId, ["delivery-requests.read"]);
        using var otherClient = CreateClient(dataSource, otherCustomer.CustomerId, ["delivery-requests.read"]);
        var ownerResponse = await ownerClient.GetAsync($"/v1/delivery-requests/{created.DeliveryRequestId}");
        var otherResponse = await otherClient.GetAsync($"/v1/delivery-requests/{created.DeliveryRequestId}");

        Assert.Equal(System.Net.HttpStatusCode.OK, ownerResponse.StatusCode);
        var response = await ownerResponse.Content.ReadFromJsonAsync<DeliveryRequestStatusResponse>();
        Assert.NotNull(response);
        Assert.Equal(created.DeliveryRequestId, response.DeliveryRequestId);
        Assert.Equal("ORDER-STATUS-1", response.ExternalReference);
        Assert.Equal("RECEIVED", response.Status);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, otherResponse.StatusCode);
    }

    private static HttpClient CreateClient(
        NpgsqlDataSource dataSource,
        string customerId,
        IReadOnlyList<string> scopes)
    {
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
                        scopes)));
            }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);
        return client;
    }

    private sealed record FixedCustomerContext(string CustomerId) : ICustomerContext;

    private sealed class FixedCredentialStore(ApiKeyCredential credential) : IApiKeyCredentialStore
    {
        public Task<ApiKeyCredential?> FindAsync(string keyId, CancellationToken cancellationToken) =>
            Task.FromResult<ApiKeyCredential?>(keyId == credential.KeyId ? credential : null);
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
        var assembly = typeof(DeliveryRequestStatusEndpointTests).Assembly;
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
