using System.Text.Json;
using Mono.Api.Domain;
using Mono.Api.Persistence;
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
        var second = await repo.CreateOrReplayAsync(
            branch.BranchId, "ORDER-STATUS-2", "status-endpoint-2", (request with { ExternalReference = "ORDER-STATUS-2" }).ToCanonicalJson());
        var third = await repo.CreateOrReplayAsync(
            branch.BranchId, "ORDER-STATUS-3", "status-endpoint-3", (request with { ExternalReference = "ORDER-STATUS-3" }).ToCanonicalJson());

        await using var ownerFactory = CreateFactory(dataSource, customer.CustomerId, ["delivery-requests.read"]);
        await using var otherFactory = CreateFactory(dataSource, otherCustomer.CustomerId, ["delivery-requests.read"]);
        using var ownerClient = ownerFactory.CreateClient();
        using var otherClient = otherFactory.CreateClient();
        ownerClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);
        otherClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);
        var ownerResponse = await ownerClient.GetAsync($"/v1/delivery-requests/{created.DeliveryRequestId}");
        var otherResponse = await otherClient.GetAsync($"/v1/delivery-requests/{created.DeliveryRequestId}");
        var unknownResponse = await ownerClient.GetAsync(
            "/v1/delivery-requests/drq_00000000000000000000000000000000");
        var firstPageResponse = await ownerClient.GetAsync("/v1/delivery-requests?page_size=2");
        var firstPageJson = await firstPageResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Store", firstPageJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Customer", firstPageJson, StringComparison.Ordinal);
        var firstPage = JsonSerializer.Deserialize<DeliveryRequestPageResponse>(firstPageJson);
        Assert.NotNull(firstPage);
        var secondPageResponse = await ownerClient.GetAsync(
            $"/v1/delivery-requests?page_size=2&cursor={firstPage.NextCursor}");
        var secondPageJson = await secondPageResponse.Content.ReadAsStringAsync();
        var secondPage = JsonSerializer.Deserialize<DeliveryRequestPageResponse>(secondPageJson);
        var foreignCursorResponse = await otherClient.GetAsync(
            $"/v1/delivery-requests?cursor={created.DeliveryRequestId}");
        var invalidSizeResponse = await ownerClient.GetAsync("/v1/delivery-requests?page_size=101");

        Assert.Equal(System.Net.HttpStatusCode.OK, ownerResponse.StatusCode);
        var responseJson = await ownerResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Store", responseJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Customer", responseJson, StringComparison.Ordinal);
        var response = JsonSerializer.Deserialize<DeliveryRequestStatusResponse>(responseJson);
        Assert.NotNull(response);
        Assert.Equal(created.DeliveryRequestId, response.DeliveryRequestId);
        Assert.Equal("ORDER-STATUS-1", response.ExternalReference);
        Assert.Equal("RECEIVED", response.Status);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, otherResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, unknownResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, firstPageResponse.StatusCode);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.NotNull(firstPage.NextCursor);
        Assert.Equal(System.Net.HttpStatusCode.OK, secondPageResponse.StatusCode);
        Assert.Single(secondPage!.Items);
        Assert.Null(secondPage.NextCursor);
        var listedIds = firstPage.Items.Concat(secondPage!.Items)
            .Select(item => item.DeliveryRequestId)
            .ToArray();
        Assert.Equal(3, listedIds.Length);
        Assert.Equal(3, listedIds.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            new[] { created.DeliveryRequestId, second.DeliveryRequestId, third.DeliveryRequestId }.Order(StringComparer.Ordinal),
            listedIds.Order(StringComparer.Ordinal));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, foreignCursorResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalidSizeResponse.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        NpgsqlDataSource dataSource,
        string customerId,
        IReadOnlyList<string> scopes) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
                     "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
                     "Mono.Api.Tests.Migrations.004_delivery_request_list_index.sql"
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
