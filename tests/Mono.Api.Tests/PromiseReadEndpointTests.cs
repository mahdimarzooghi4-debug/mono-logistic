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
public sealed class PromiseReadEndpointTests
{
    private const string ApiKey =
        "mono_live_0123456789abcdef_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Promise_read_is_authenticated_safe_and_tenant_scoped()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);

        var customer = Customer.Create("Mono Promise Read Test", "ACTIVE");
        var otherCustomer = Customer.Create("Mono Other Promise Read Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        await InsertCustomerAsync(dataSource, otherCustomer);

        var branch = Branch.Create(customer.CustomerId, "Promise Read Branch", "ACTIVE");
        var branches = new CustomerBranchRepository(dataSource, new FixedCustomerContext(customer.CustomerId));
        await branches.CreateBranchAsync(branch);

        var requests = new PostgresDeliveryRequestRepository(dataSource, new FixedCustomerContext(customer.CustomerId));
        var input = new DeliveryRequestInput
        {
            ExternalReference = "PROMISE-READ-1",
            BranchId = branch.BranchId,
            Pickup = new DeliveryLocationInput { AddressText = "Store" },
            Dropoff = new DeliveryLocationInput { AddressText = "Customer" },
            Shipment = new DeliveryShipmentInput { ShipmentModel = "BAG_BASED", BagCount = 1, LoadClass = "S" },
            RequestedService = "URBAN_DELIVERY",
            RequestedPriority = "STANDARD"
        };
        var committedRequest = await requests.CreateOrReplayAsync(
            branch.BranchId, input.ExternalReference, "promise-read-1", input.ToCanonicalJson());
        var emptyRequest = await requests.CreateOrReplayAsync(
            branch.BranchId, "PROMISE-READ-2", "promise-read-2",
            (input with { ExternalReference = "PROMISE-READ-2" }).ToCanonicalJson());

        var promiseRepository = new PostgresPromiseCommitRepository(
            dataSource, new FixedCustomerContext(customer.CustomerId));
        var proposal = new PromiseCommitProposal(
            $"prm_{Guid.NewGuid():N}",
            committedRequest.DeliveryRequestId,
            "promise-read-decision",
            1,
            DateTimeOffset.UtcNow,
            DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600),
            FeasibilityOutcome.Accepted,
            TimeSpan.FromMinutes(5),
            "buffer-v1",
            new Dictionary<string, string> { ["promise"] = "v1" },
            ["PROMISE_FEASIBLE"]);
        await promiseRepository.CommitAsync(proposal);
        var revision = proposal with
        {
            DecisionId = "promise-read-decision-2",
            Version = 2,
            CommittedDeliveryAt = proposal.CommittedDeliveryAt.AddHours(1),
            Outcome = FeasibilityOutcome.AlternativePromise
        };
        await promiseRepository.CommitAsync(revision);

        await using var ownerFactory = CreateFactory(dataSource, customer.CustomerId, ["delivery-requests.read"]);
        await using var otherFactory = CreateFactory(dataSource, otherCustomer.CustomerId, ["delivery-requests.read"]);
        await using var writeOnlyFactory = CreateFactory(dataSource, customer.CustomerId, ["delivery-requests.write"]);
        using var ownerClient = ownerFactory.CreateClient();
        using var otherClient = otherFactory.CreateClient();
        using var writeOnlyClient = writeOnlyFactory.CreateClient();
        ownerClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);
        otherClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);
        writeOnlyClient.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);

        var path = $"/v1/delivery-requests/{committedRequest.DeliveryRequestId}/promise";
        var ownerResponse = await ownerClient.GetAsync(path);
        var otherResponse = await otherClient.GetAsync(path);
        var missingResponse = await ownerClient.GetAsync(
            $"/v1/delivery-requests/{emptyRequest.DeliveryRequestId}/promise");
        var unauthorizedResponse = await writeOnlyClient.GetAsync(path);
        var firstHistoryPage = await ownerClient.GetAsync(
            $"/v1/delivery-requests/{committedRequest.DeliveryRequestId}/promise/versions?page_size=1");
        var firstHistoryJson = await firstHistoryPage.Content.ReadAsStringAsync();
        using var firstHistoryDocument = JsonDocument.Parse(firstHistoryJson);
        var firstHistory = firstHistoryDocument.RootElement;
        var beforeVersion = firstHistory.GetProperty("next_before_version").GetInt32();
        var secondHistoryPage = await ownerClient.GetAsync(
            $"/v1/delivery-requests/{committedRequest.DeliveryRequestId}/promise/versions?page_size=1&before_version={beforeVersion}");
        var foreignHistoryPage = await otherClient.GetAsync(
            $"/v1/delivery-requests/{committedRequest.DeliveryRequestId}/promise/versions");
        var missingHistoryPage = await ownerClient.GetAsync(
            $"/v1/delivery-requests/{emptyRequest.DeliveryRequestId}/promise/versions");
        var invalidHistoryPage = await ownerClient.GetAsync(
            $"/v1/delivery-requests/{committedRequest.DeliveryRequestId}/promise/versions?before_version=0");

        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        var json = await ownerResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var body = document.RootElement;
        Assert.Equal(proposal.PromiseId, body.GetProperty("promise_id").GetString());
        Assert.Equal(committedRequest.DeliveryRequestId, body.GetProperty("delivery_request_id").GetString());
        Assert.Equal(2, body.GetProperty("version").GetInt32());
        Assert.Equal("ALTERNATIVE_PROMISE", body.GetProperty("outcome").GetString());
        Assert.Equal(revision.CommittedDeliveryAt, body.GetProperty("committed_delivery_at").GetDateTimeOffset());
        Assert.DoesNotContain("safety_buffer", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reason_codes", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("policy_versions", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.NotFound, otherResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unauthorizedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, firstHistoryPage.StatusCode);
        var firstItem = firstHistory.GetProperty("items")[0];
        Assert.Equal(2, firstItem.GetProperty("version").GetInt32());
        Assert.Equal("ALTERNATIVE_PROMISE", firstItem.GetProperty("outcome").GetString());
        Assert.DoesNotContain("safety_buffer", firstHistoryJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reason_codes", firstHistoryJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.OK, secondHistoryPage.StatusCode);
        using var secondHistoryDocument = JsonDocument.Parse(await secondHistoryPage.Content.ReadAsStringAsync());
        Assert.Equal(1, secondHistoryDocument.RootElement.GetProperty("items")[0].GetProperty("version").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, foreignHistoryPage.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missingHistoryPage.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidHistoryPage.StatusCode);
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
        var assembly = typeof(PromiseReadEndpointTests).Assembly;
        foreach (var resourceName in new[]
                 {
                     "Mono.Api.Tests.Migrations.001_customer_branch.sql",
                     "Mono.Api.Tests.Migrations.002_customer_api_keys.sql",
                     "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
                     "Mono.Api.Tests.Migrations.018_promise_commit.sql"
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
