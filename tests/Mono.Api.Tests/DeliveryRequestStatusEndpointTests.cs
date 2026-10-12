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
        var committedAt = DateTimeOffset.FromUnixTimeSeconds(
            DateTimeOffset.UtcNow.AddHours(3).ToUnixTimeSeconds());
        var missionId = await InsertMissionAsync(dataSource, customer.CustomerId, created.DeliveryRequestId, committedAt);

        await using var ownerFactory = CreateFactory(dataSource, customer.CustomerId, ["delivery-requests.read"]);
        await using var otherFactory = CreateFactory(dataSource, otherCustomer.CustomerId, ["delivery-requests.read"]);
        using var ownerClient = ownerFactory.CreateClient();
        using var otherClient = otherFactory.CreateClient();
        ownerClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);
        otherClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);
        var ownerResponse = await ownerClient.GetAsync($"/v1/delivery-requests/{created.DeliveryRequestId}");
        var unconfirmedResponse = await ownerClient.GetAsync($"/v1/delivery-requests/{second.DeliveryRequestId}");
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
        Assert.Equal(missionId, response.MissionId);
        Assert.Equal("PROMISED", response.MissionStatus);
        Assert.Equal(committedAt, response.CommittedDeliveryAt);
        Assert.Equal(System.Net.HttpStatusCode.OK, unconfirmedResponse.StatusCode);
        var unconfirmedJson = await unconfirmedResponse.Content.ReadAsStringAsync();
        var unconfirmed = JsonSerializer.Deserialize<DeliveryRequestStatusResponse>(unconfirmedJson);
        Assert.NotNull(unconfirmed);
        Assert.Null(unconfirmed.MissionId);
        Assert.Null(unconfirmed.MissionStatus);
        Assert.Null(unconfirmed.CommittedDeliveryAt);
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
        var confirmedHistoryItem = firstPage.Items.Concat(secondPage!.Items)
            .Single(item => item.DeliveryRequestId == created.DeliveryRequestId);
        Assert.Equal(missionId, confirmedHistoryItem.MissionId);
        Assert.Equal("PROMISED", confirmedHistoryItem.MissionStatus);
        Assert.Equal(committedAt, confirmedHistoryItem.CommittedDeliveryAt);
        Assert.All(firstPage.Items.Concat(secondPage.Items).Where(item => item.DeliveryRequestId != created.DeliveryRequestId),
            item =>
            {
                Assert.Null(item.MissionId);
                Assert.Null(item.MissionStatus);
                Assert.Null(item.CommittedDeliveryAt);
            });
        Assert.Equal(
            new[] { created.DeliveryRequestId, second.DeliveryRequestId, third.DeliveryRequestId }.Order(StringComparer.Ordinal),
            listedIds.Order(StringComparer.Ordinal));
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, foreignCursorResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalidSizeResponse.StatusCode);
    }

    private static async Task<string> InsertMissionAsync(
        NpgsqlDataSource dataSource,
        string customerId,
        string requestId,
        DateTimeOffset committedAt)
    {
        var quoteId = "qte_" + Guid.NewGuid().ToString("N");
        var promiseId = "prm_" + Guid.NewGuid().ToString("N");
        var missionId = "mis_" + Guid.NewGuid().ToString("N");
        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        var decisionId = "decision-" + Guid.NewGuid().ToString("N");
        await using var connection = await dataSource.OpenConnectionAsync();

        await using (var quote = new NpgsqlCommand("""
            INSERT INTO delivery_quote_versions (
                customer_id, delivery_request_id, quote_id, version, issued_at, expires_at,
                snapshot_hash, snapshot, created_at)
            VALUES (@customer_id, @request_id, @quote_id, 1, @issued_at, @expires_at,
                @hash, '{}'::jsonb, @issued_at);
            """, connection))
        {
            quote.Parameters.AddWithValue("customer_id", customerId);
            quote.Parameters.AddWithValue("request_id", requestId);
            quote.Parameters.AddWithValue("quote_id", quoteId);
            quote.Parameters.AddWithValue("issued_at", committedAt.AddHours(-1));
            quote.Parameters.AddWithValue("expires_at", committedAt.AddHours(1));
            quote.Parameters.AddWithValue("hash", new string('a', 64));
            await quote.ExecuteNonQueryAsync();
        }

        await using (var promise = new NpgsqlCommand("""
            INSERT INTO promises (
                promise_id, customer_id, delivery_request_id, current_version, latest_decision_id,
                committed_delivery_at, outcome, safety_buffer_ticks, safety_buffer_policy_version,
                policy_versions, reason_codes, updated_at)
            VALUES (@promise_id, @customer_id, @request_id, 1, @decision_id,
                @committed_at, 'ACCEPTED', 0, 'test-policy', '{}'::jsonb, '[]'::jsonb, @committed_at);
            """, connection))
        {
            promise.Parameters.AddWithValue("promise_id", promiseId);
            promise.Parameters.AddWithValue("customer_id", customerId);
            promise.Parameters.AddWithValue("request_id", requestId);
            promise.Parameters.AddWithValue("decision_id", decisionId);
            promise.Parameters.AddWithValue("committed_at", committedAt);
            await promise.ExecuteNonQueryAsync();
        }

        await using (var version = new NpgsqlCommand("""
            INSERT INTO promise_versions (
                promise_id, customer_id, version, decision_id, proposal_hash, proposal, event_id, created_at)
            VALUES (@promise_id, @customer_id, 1, @decision_id, @hash,
                jsonb_build_object('delivery_request_id', @request_id), @event_id, @created_at);
            """, connection))
        {
            version.Parameters.AddWithValue("promise_id", promiseId);
            version.Parameters.AddWithValue("customer_id", customerId);
            version.Parameters.AddWithValue("request_id", requestId);
            version.Parameters.AddWithValue("decision_id", decisionId);
            version.Parameters.AddWithValue("hash", new string('b', 64));
            version.Parameters.AddWithValue("event_id", eventId);
            version.Parameters.AddWithValue("created_at", committedAt);
            await version.ExecuteNonQueryAsync();
        }

        await using var mission = new NpgsqlCommand("""
            INSERT INTO missions (
                mission_id, customer_id, delivery_request_id, quote_id, quote_version,
                promise_id, promise_version, status, committed_delivery_at, created_at)
            VALUES (@mission_id, @customer_id, @request_id, @quote_id, 1,
                @promise_id, 1, 'PROMISED', @committed_at, @created_at);
            """, connection);
        mission.Parameters.AddWithValue("mission_id", missionId);
        mission.Parameters.AddWithValue("customer_id", customerId);
        mission.Parameters.AddWithValue("request_id", requestId);
        mission.Parameters.AddWithValue("quote_id", quoteId);
        mission.Parameters.AddWithValue("promise_id", promiseId);
        mission.Parameters.AddWithValue("committed_at", committedAt);
        mission.Parameters.AddWithValue("created_at", committedAt);
        await mission.ExecuteNonQueryAsync();
        return missionId;
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
                     "Mono.Api.Tests.Migrations.004_delivery_request_list_index.sql",
            "Mono.Api.Tests.Migrations.005_customer_service_configurations.sql",
                     "Mono.Api.Tests.Migrations.018_promise_commit.sql",
                     "Mono.Api.Tests.Migrations.019_delivery_quote_versions.sql",
                     "Mono.Api.Tests.Migrations.020_delivery_quote_issuance_idempotency.sql",
                     "Mono.Api.Tests.Migrations.021_mission_confirmation.sql"
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
