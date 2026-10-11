using System.Net;
using System.Net.Http.Json
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class MissionConfirmationEndpointTests
{
    private const string ApiKey =
        "mono_live_0123456789abcdef_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Confirmation_creates_one_tenant_scoped_mission_and_outbox_event_and_replays_exact_request()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);

        var customer = Customer.Create("Mission Confirmation Test", "ACTIVE");
        var otherCustomer = Customer.Create("Other Mission Confirmation Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        await InsertCustomerAsync(dataSource, otherCustomer);
        var branch = Branch.Create(customer.CustomerId, "Confirmation Branch", "ACTIVE");
        var branches = new CustomerBranchRepository(dataSource, new FixedCustomerContext(customer.CustomerId));
        await branches.CreateBranchAsync(branch);

        var requestInput = new DeliveryRequestInput
        {
            ExternalReference = "CONFIRM-ORDER-1",
            BranchId = branch.BranchId,
            Pickup = new DeliveryLocationInput { AddressText = "Pickup" },
            Dropoff = new DeliveryLocationInput { AddressText = "Dropoff" },
            Shipment = new DeliveryShipmentInput { ShipmentModel = "BAG_BASED", BagCount = 1, LoadClass = "S" },
            RequestedService = "URBAN_DELIVERY",
            RequestedPriority = "STANDARD"
        };
        var deliveryRequests = new PostgresDeliveryRequestRepository(dataSource, new FixedCustomerContext(customer.CustomerId));
        var created = await deliveryRequests.CreateOrReplayAsync(
            branch.BranchId, requestInput.ExternalReference, "confirm-intake-1", requestInput.ToCanonicalJson());

        var quoteId = "qte_0123456789abcdef0123456789abcdef";
        var promiseId = "prm_0123456789abcdef0123456789abcdef";
        var committedAt = DateTimeOffset.UtcNow.AddHours(2);
        await InsertQuoteAsync(dataSource, customer.CustomerId, created.DeliveryRequestId, quoteId);
        var expiredQuoteId = "qte_ffffffffffffffffffffffffffffffff";
        await InsertQuoteAsync(dataSource, customer.CustomerId, created.DeliveryRequestId, expiredQuoteId,
            2, DateTimeOffset.UtcNow.AddMinutes(-1));
        await InsertPromiseAsync(dataSource, customer.CustomerId, created.DeliveryRequestId, promiseId, committedAt);

        await using var ownerFactory = CreateFactory(dataSource, customer.CustomerId, ["delivery-requests.write", "delivery-requests.read"]);
        await using var otherFactory = CreateFactory(dataSource, otherCustomer.CustomerId, ["delivery-requests.write", "delivery-requests.read"]);
        await using var readOnlyFactory = CreateFactory(dataSource, customer.CustomerId, ["delivery-requests.read"]);
        using var ownerClient = ownerFactory.CreateClient();
        using var otherClient = otherFactory.CreateClient();
        using var readOnlyClient = readOnlyFactory.CreateClient();
        ownerClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);
        otherClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);
        readOnlyClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);

        var path = $"/v1/delivery-requests/{created.DeliveryRequestId}/confirm";
        var body = new MissionConfirmationInput(quoteId, 1);
        var first = await PostAsync(ownerClient, path, "confirm-mission-1", body);
        var firstJson = await first.Content.ReadAsStringAsync();
        var replay = await PostAsync(ownerClient, path, "confirm-mission-1", body);
        var replayJson = await replay.Content.ReadAsStringAsync();
        var changedReplay = await PostAsync(ownerClient, path, "confirm-mission-1", body with { PromiseVersion = 2 });
        var foreignTenant = await PostAsync(otherClient, path, "foreign-confirm", body);
        var readOnly = await PostAsync(readOnlyClient, path, "read-only-confirm", body);
        var invalidPromise = await PostAsync(ownerClient, path, "invalid-promise", body with { PromiseVersion = 3 });
        var invalidQuote = await PostAsync(ownerClient, path, "invalid-quote", body with { QuoteId = "qte_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" });
        var expiredQuote = await PostAsync(ownerClient, path, "expired-quote", body with { QuoteId = expiredQuoteId });

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var firstDocument = JsonDocument.Parse(firstJson);
        var mission = firstDocument.RootElement;
        var missionId = mission.GetProperty("mission_id").GetString();
        Assert.StartsWith("mis_", missionId, StringComparison.Ordinal);
        var missionResponse = await ownerClient.GetAsync($"/v1/missions/{missionId}");
        var foreignMissionResponse = await otherClient.GetAsync($"/v1/missions/{missionId}");
        Assert.Equal(created.DeliveryRequestId, mission.GetProperty("delivery_request_id").GetString());
        Assert.Equal(quoteId, mission.GetProperty("quote_id").GetString());
        Assert.Equal(promiseId, mission.GetProperty("promise_id").GetString());
        Assert.Equal(1, mission.GetProperty("promise_version").GetInt32());
        Assert.Equal("PROMISED", mission.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var replayDocument = JsonDocument.Parse(replayJson);
        Assert.Equal(missionId, replayDocument.RootElement.GetProperty("mission_id").GetString());
        Assert.Equal(HttpStatusCode.Conflict, changedReplay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, missionResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreignMissionResponse.StatusCode);
        var missionJson = await missionResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Pickup", missionJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Dropoff", missionJson, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, foreignTenant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, readOnly.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invalidPromise.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, invalidQuote.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, expiredQuote.StatusCode);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var countCommand = new NpgsqlCommand("""
            SELECT
              (SELECT count(*) FROM missions WHERE customer_id = @customer_id AND delivery_request_id = @request_id),
              (SELECT count(*) FROM mission_confirmation_idempotency WHERE customer_id = @customer_id),
              (SELECT count(*) FROM outbox_events WHERE customer_id = @customer_id AND event_type = 'mission.created');
            """, connection);
        countCommand.Parameters.AddWithValue("customer_id", customer.CustomerId);
        countCommand.Parameters.AddWithValue("request_id", created.DeliveryRequestId);
        await using var counts = await countCommand.ExecuteReaderAsync();
        Assert.True(await counts.ReadAsync());
        Assert.Equal(1L, counts.GetInt64(0));
        Assert.Equal(1L, counts.GetInt64(1));
        Assert.Equal(1L, counts.GetInt64(2));
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string path, string idempotencyKey, MissionConfirmationInput body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
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
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO customers (customer_id, legal_name, status, created_at)
            VALUES (@customer_id, @legal_name, @status, @created_at);
            """, connection);
        command.Parameters.AddWithValue("customer_id", customer.CustomerId);
        command.Parameters.AddWithValue("legal_name", customer.LegalName);
        command.Parameters.AddWithValue("status", customer.Status);
        command.Parameters.AddWithValue("created_at", customer.CreatedAt);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertQuoteAsync(
        NpgsqlDataSource dataSource, string customerId, string requestId, string quoteId,
        int version = 1, DateTimeOffset? expiresAt = null)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO delivery_quote_versions (
                customer_id, delivery_request_id, quote_id, version, issued_at, expires_at,
                snapshot_hash, snapshot, created_at)
            VALUES (@customer_id, @request_id, @quote_id, @version, @issued_at, @expires_at,
                @hash, '{}'::jsonb, @issued_at);
            """, connection);
        command.Parameters.AddWithValue("customer_id", customerId);
        command.Parameters.AddWithValue("request_id", requestId);
        command.Parameters.AddWithValue("quote_id", quoteId);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("issued_at", DateTimeOffset.UtcNow.AddMinutes(-2));
        command.Parameters.AddWithValue("expires_at", expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(10));
        command.Parameters.AddWithValue("hash", new string('a', 64));
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertPromiseAsync(
        NpgsqlDataSource dataSource, string customerId, string requestId, string promiseId,
        DateTimeOffset committedAt)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await using (var promise = new NpgsqlCommand("""
            INSERT INTO promises (
                promise_id, customer_id, delivery_request_id, current_version, latest_decision_id,
                committed_delivery_at, outcome, safety_buffer_ticks, safety_buffer_policy_version,
                policy_versions, reason_codes, updated_at)
            VALUES (@promise_id, @customer_id, @request_id, 1, 'decision-confirm-1',
                @committed_at, 'ACCEPTED', 0, 'test-policy', '{}'::jsonb, '[]'::jsonb, @committed_at);
            """, connection))
        {
            promise.Parameters.AddWithValue("promise_id", promiseId);
            promise.Parameters.AddWithValue("customer_id", customerId);
            promise.Parameters.AddWithValue("request_id", requestId);
            promise.Parameters.AddWithValue("committed_at", committedAt);
            await promise.ExecuteNonQueryAsync();
        }

        await using var version = new NpgsqlCommand("""
            INSERT INTO promise_versions (
                promise_id, customer_id, version, decision_id, proposal_hash, proposal, event_id, created_at)
            VALUES (@promise_id, @customer_id, 1, 'decision-confirm-1', @hash,
                jsonb_build_object('delivery_request_id', @request_id), @event_id, @created_at);
            """, connection);
        version.Parameters.AddWithValue("promise_id", promiseId);
        version.Parameters.AddWithValue("customer_id", customerId);
        version.Parameters.AddWithValue("request_id", requestId);
        version.Parameters.AddWithValue("hash", new string('b', 64));
        version.Parameters.AddWithValue("event_id", "evt_0123456789abcdef0123456789abcdef");
        version.Parameters.AddWithValue("created_at", committedAt);
        await version.ExecuteNonQueryAsync();
    }

    private static async Task ApplyMigrationsAsync(NpgsqlDataSource dataSource)
    {
        var assembly = typeof(MissionConfirmationEndpointTests).Assembly;
        foreach (var resourceName in new[]
                 {
                     "Mono.Api.Tests.Migrations.001_customer_branch.sql",
                     "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
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
