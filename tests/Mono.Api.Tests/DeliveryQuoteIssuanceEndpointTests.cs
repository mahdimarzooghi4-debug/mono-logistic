using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class DeliveryQuoteIssuanceEndpointTests
{
    private const string ApiKey =
        "mono_live_0123456789abcdef_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Issue_route_returns_same_quote_on_retry_and_conflicts_when_key_is_reused_for_changed_input()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var customer = Customer.Create("Mono Quote Issuance API Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        var branch = Branch.Create(customer.CustomerId, "Quote Issuance API Branch", "ACTIVE");
        await InsertBranchAsync(dataSource, branch);

        var context = new FixedCustomerContext(customer.CustomerId);
        var intake = new PostgresDeliveryRequestRepository(dataSource, context);
        var request = await intake.CreateOrReplayAsync(
            branch.BranchId,
            "QUOTE-ISSUE-API-1",
            "quote-issue-api-intake",
            new DeliveryRequestInput
            {
                ExternalReference = "QUOTE-ISSUE-API-1",
                BranchId = branch.BranchId,
                Pickup = new DeliveryLocationInput { AddressText = "Store" },
                Dropoff = new DeliveryLocationInput { AddressText = "Customer" },
                Shipment = new DeliveryShipmentInput
                {
                    ShipmentModel = "BAG_BASED",
                    BagCount = 1,
                    LoadClass = "S"
                },
                RequestedService = "URBAN_DELIVERY",
                RequestedPriority = "STANDARD"
            }.Normalize().ToCanonicalJson());

        var verifier = new VerifiedDistanceSource();
        await using var factory = CreateFactory(dataSource, customer.CustomerId, verifier);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);

        var path = $"/v1/delivery-requests/{request.DeliveryRequestId}/quote";
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(15);
        var observedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var firstResponse = await SendIssueAsync(client, path, "quote-issue-key", expiresAt, observedAt, 2_500m);
        var firstJson = await firstResponse.Content.ReadAsStringAsync();
        var retryResponse = await SendIssueAsync(client, path, "quote-issue-key", expiresAt, observedAt, 2_500m);
        var retryJson = await retryResponse.Content.ReadAsStringAsync();
        var changedInputResponse = await SendIssueAsync(
            client, path, "quote-issue-key", expiresAt, observedAt, 3_000m);
        var changedInputJson = await changedInputResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        using var firstDocument = JsonDocument.Parse(firstJson);
        using var retryDocument = JsonDocument.Parse(retryJson);
        var quoteId = firstDocument.RootElement.GetProperty("quote_id").GetString();
        Assert.Equal(quoteId, retryDocument.RootElement.GetProperty("quote_id").GetString());
        Assert.Equal(1, retryDocument.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(35_000, retryDocument.RootElement.GetProperty("fare_toman").GetInt32());
        Assert.DoesNotContain("source_attestation", firstJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("source_id", firstJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payload_sha256", firstJson, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.Conflict, changedInputResponse.StatusCode);
        using var conflictDocument = JsonDocument.Parse(changedInputJson);
        Assert.Equal("IDEMPOTENCY_CONFLICT",
            conflictDocument.RootElement.GetProperty("error").GetProperty("code").GetString());

        Assert.Equal(1, verifier.CallCount);
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*)
            FROM delivery_quote_versions
            WHERE customer_id = @customer_id AND delivery_request_id = @request_id;
            """, connection);
        count.Parameters.AddWithValue("customer_id", customer.CustomerId);
        count.Parameters.AddWithValue("request_id", request.DeliveryRequestId);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);

        await using var outbox = new NpgsqlCommand("""
            SELECT count(*), min(event_type), min(aggregate_type), min(sequence),
                   min(data->>'delivery_request_id')
            FROM outbox_events
            WHERE customer_id = @customer_id AND aggregate_id = @quote_id;
            """, connection);
        outbox.Parameters.AddWithValue("customer_id", customer.CustomerId);
        outbox.Parameters.AddWithValue("quote_id", quoteId!);
        await using var eventReader = await outbox.ExecuteReaderAsync();
        Assert.True(await eventReader.ReadAsync());
        Assert.Equal(1L, eventReader.GetInt64(0));
        Assert.Equal("quote.issued", eventReader.GetString(1));
        Assert.Equal("quote", eventReader.GetString(2));
        Assert.Equal(1L, eventReader.GetInt64(3));
        Assert.Equal(request.DeliveryRequestId, eventReader.GetString(4));
    }

    private static async Task<HttpResponseMessage> SendIssueAsync(
        HttpClient client,
        string path,
        string idempotencyKey,
        DateTimeOffset expiresAt,
        DateTimeOffset observedAt,
        decimal distanceMeters)
    {
        var body = new
        {
            expires_at = expiresAt.ToString("O"),
            distance_evidence = new
            {
                evidence_id = "quote-api-distance-1",
                observed_at = observedAt.ToString("O"),
                distance_meters = distanceMeters,
                source_id = "test-maps-source",
                source_attestation = "test-attestation"
            }
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(message);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        NpgsqlDataSource dataSource,
        string customerId,
        IHanaDistanceEvidenceSourceVerifier verifier) =>
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
                        ["delivery-requests.write", "delivery-requests.read"])));
                services.RemoveAll<IHanaDistanceEvidenceSourceVerifier>();
                services.AddSingleton<IHanaDistanceEvidenceSourceVerifier>(verifier);
            }));

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
        var assembly = typeof(DeliveryQuoteIssuanceEndpointTests).Assembly;
        foreach (var resourceName in new[]
                 {
                     "Mono.Api.Tests.Migrations.001_customer_branch.sql",
                     "Mono.Api.Tests.Migrations.002_customer_api_keys.sql",
                     "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
                     "Mono.Api.Tests.Migrations.018_promise_commit.sql",
                     "Mono.Api.Tests.Migrations.019_delivery_quote_versions.sql",
                     "Mono.Api.Tests.Migrations.020_delivery_quote_issuance_idempotency.sql",
                     "Mono.Api.Tests.Migrations.028_quote_issued_event.sql",
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

    private sealed record FixedCustomerContext(string CustomerId) : ICustomerContext;

    private sealed class FixedCredentialStore(ApiKeyCredential credential) : IApiKeyCredentialStore
    {
        public Task<ApiKeyCredential?> FindAsync(string keyId, CancellationToken cancellationToken) =>
            Task.FromResult<ApiKeyCredential?>(keyId == credential.KeyId ? credential : null);
    }

    private sealed class VerifiedDistanceSource : IHanaDistanceEvidenceSourceVerifier
    {
        public int CallCount { get; private set; }

        public Task<HanaDistanceEvidenceVerification> VerifyAsync(
            string customerId,
            HanaDistancePricingEvidence evidence,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new HanaDistanceEvidenceVerification(
                CallCount == 1
                    ? HanaDistanceEvidenceVerificationStatus.Verified
                    : HanaDistanceEvidenceVerificationStatus.Unverified,
                CallCount == 1 ? "TEST_VERIFIED" : "SOURCE_UNAVAILABLE"));
        }
    }
}
