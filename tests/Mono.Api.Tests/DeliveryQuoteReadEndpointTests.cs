using System.Collections.Immutable;
using System.Net;
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
public sealed class DeliveryQuoteReadEndpointTests
{
    private const string ApiKey =
        "mono_live_0123456789abcdef_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Quote_reads_are_authenticated_tenant_scoped_paginated_and_hide_internal_evidence()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);

        var customer = Customer.Create("Mono Quote Read Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        var branch = Branch.Create(customer.CustomerId, "Quote Read Branch", "ACTIVE");
        await InsertBranchAsync(dataSource, branch);

        var context = new FixedCustomerContext(customer.CustomerId);
        var intake = new PostgresDeliveryRequestRepository(dataSource, context);
        var payload = new DeliveryRequestInput
        {
            ExternalReference = "QUOTE-READ-1",
            BranchId = branch.BranchId,
            Pickup = new DeliveryLocationInput { AddressText = "Store" },
            Dropoff = new DeliveryLocationInput { AddressText = "Customer" },
            Shipment = new DeliveryShipmentInput { ShipmentModel = "BAG_BASED", BagCount = 1, LoadClass = "S" },
            RequestedService = "URBAN_DELIVERY",
            RequestedPriority = "STANDARD"
        }.Normalize().ToCanonicalJson();
        var quotedRequest = await intake.CreateOrReplayAsync(
            branch.BranchId, "QUOTE-READ-1", "quote-read-1", payload);
        var emptyRequest = await intake.CreateOrReplayAsync(
            branch.BranchId, "QUOTE-READ-EMPTY", "quote-read-empty",
            new DeliveryRequestInput
            {
                ExternalReference = "QUOTE-READ-EMPTY",
                BranchId = branch.BranchId,
                Pickup = new DeliveryLocationInput { AddressText = "Store" },
                Dropoff = new DeliveryLocationInput { AddressText = "Customer" },
                Shipment = new DeliveryShipmentInput { ShipmentModel = "BAG_BASED", BagCount = 1, LoadClass = "S" },
                RequestedService = "URBAN_DELIVERY",
                RequestedPriority = "STANDARD"
            }.Normalize().ToCanonicalJson());

        var quoteRepository = new PostgresDeliveryQuoteRepository(dataSource, context);
        var first = Quote(customer.CustomerId, quotedRequest.DeliveryRequestId, 1, 1_500m,
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(14));
        var expired = Quote(customer.CustomerId, quotedRequest.DeliveryRequestId, 2, 2_500m,
            DateTimeOffset.UtcNow.AddMinutes(-15), DateTimeOffset.UtcNow.AddMinutes(-1));
        Assert.Equal(DeliveryQuoteWriteOutcome.Persisted, (await quoteRepository.PersistAsync(first)).Outcome);
        Assert.Equal(DeliveryQuoteWriteOutcome.Persisted, (await quoteRepository.PersistAsync(expired)).Outcome);

        var otherCustomerId = Customer.Create("Other Quote Read Tenant", "ACTIVE").CustomerId;
        await using var ownerFactory = CreateFactory(dataSource, customer.CustomerId, ["delivery-requests.read"]);
        await using var otherFactory = CreateFactory(dataSource, otherCustomerId, ["delivery-requests.read"]);
        await using var writeOnlyFactory = CreateFactory(dataSource, customer.CustomerId, ["delivery-requests.write"]);
        using var ownerClient = ownerFactory.CreateClient();
        using var otherClient = otherFactory.CreateClient();
        using var writeOnlyClient = writeOnlyFactory.CreateClient();
        ownerClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);
        otherClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);
        writeOnlyClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);

        var path = $"/v1/delivery-requests/{quotedRequest.DeliveryRequestId}/quote";
        var currentResponse = await ownerClient.GetAsync(path);
        var currentJson = await currentResponse.Content.ReadAsStringAsync();
        using var currentDocument = JsonDocument.Parse(currentJson);
        var current = currentDocument.RootElement;
        var historyResponse = await ownerClient.GetAsync(path + "/versions?page_size=1");
        var historyJson = await historyResponse.Content.ReadAsStringAsync();
        using var historyDocument = JsonDocument.Parse(historyJson);
        var history = historyDocument.RootElement;
        var cursor = history.GetProperty("next_before_version").GetInt32();
        var earlierPage = await ownerClient.GetAsync(path + $"/versions?page_size=1&before_version={cursor}");
        var foreignResponse = await otherClient.GetAsync(path);
        var missingResponse = await ownerClient.GetAsync(
            $"/v1/delivery-requests/{emptyRequest.DeliveryRequestId}/quote");
        var deniedResponse = await writeOnlyClient.GetAsync(path);
        var invalidPageResponse = await ownerClient.GetAsync(path + "/versions?page_size=101");
        var unauthenticatedResponse = await ownerFactory.CreateClient().GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, currentResponse.StatusCode);
        Assert.Equal(2, current.GetProperty("version").GetInt32());
        Assert.True(current.GetProperty("is_expired").GetBoolean());
        Assert.Equal(35_000, current.GetProperty("fare_toman").GetInt32());
        Assert.DoesNotContain("policy_versions", currentJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit_evidence", currentJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        Assert.Equal(2, history.GetProperty("items")[0].GetProperty("version").GetInt32());
        Assert.DoesNotContain("policy_versions", historyJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("audit_evidence", historyJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, earlierPage.StatusCode);
        using var earlierDocument = JsonDocument.Parse(await earlierPage.Content.ReadAsStringAsync());
        Assert.Equal(1, earlierDocument.RootElement.GetProperty("items")[0].GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, foreignResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidPageResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticatedResponse.StatusCode);
    }

    private static DeliveryQuote Quote(
        string customerId,
        string requestId,
        int version,
        decimal distanceMeters,
        DateTimeOffset issuedAt,
        DateTimeOffset expiresAt) =>
        new(
            "quote_read_11111111111111111111111111111111",
            customerId,
            requestId,
            version,
            issuedAt,
            expiresAt,
            HanaDistancePricingV1.Calculate(distanceMeters),
            ImmutableDictionary<string, string>.Empty.Add("pricing", HanaDistancePricingV1.PolicyVersion),
            ImmutableArray.Create("internal-evidence-must-not-be-returned"));

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

    private static async Task InsertBranchAsync(NpgsqlDataSource dataSource, Branch branch)
    {
        const string sql = """
            INSERT INTO branches (branch_id, customer_id, name, status)
            VALUES (@branch_id, @customer_id, @name, @status);
            """;
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("branch_id", branch.BranchId);
        command.Parameters.AddWithValue("customer_id", branch.CustomerId);
        command.Parameters.AddWithValue("name", branch.Name);
        command.Parameters.AddWithValue("status", branch.Status);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task ApplyMigrationsAsync(NpgsqlDataSource dataSource)
    {
        var assembly = typeof(DeliveryQuoteReadEndpointTests).Assembly;
        foreach (var resourceName in new[]
                 {
                     "Mono.Api.Tests.Migrations.001_customer_branch.sql",
                     "Mono.Api.Tests.Migrations.002_customer_api_keys.sql",
                     "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
                     "Mono.Api.Tests.Migrations.018_promise_commit.sql",
                     "Mono.Api.Tests.Migrations.019_delivery_quote_versions.sql"
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
