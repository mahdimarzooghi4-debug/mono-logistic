using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Mono.Api.Api;
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
public sealed class PromiseCommitEndpointTests
{
    private const string ApiKey =
        "mono_live_0123456789abcdef_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Promise_commit_api_evaluates_verifies_and_persists_idempotently()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var customer = Customer.Create("Mono Promise Commit API Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        var branch = Branch.Create(customer.CustomerId, "Promise Commit API Branch", "ACTIVE");
        await InsertBranchAsync(dataSource, branch);

        var requests = new PostgresDeliveryRequestRepository(
            dataSource, new FixedCustomerContext(customer.CustomerId));
        var requestInput = new DeliveryRequestInput
        {
            ExternalReference = "PROMISE-COMMIT-API-1",
            BranchId = branch.BranchId,
            Pickup = new DeliveryLocationInput { AddressText = "Store" },
            Dropoff = new DeliveryLocationInput { AddressText = "Customer" },
            Shipment = new DeliveryShipmentInput { ShipmentModel = "BAG_BASED", BagCount = 1, LoadClass = "S" },
            RequestedService = "URBAN_DELIVERY",
            RequestedPriority = "STANDARD"
        };
        var request = await requests.CreateOrReplayAsync(
            branch.BranchId, requestInput.ExternalReference, "promise-commit-intake",
            requestInput.Normalize().ToCanonicalJson());

        var verifier = new VerifiedFeasibilitySource();
        await using var factory = CreateFactory(dataSource, customer.CustomerId, verifier);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);

        var deliveryAt = DateTimeOffset.UtcNow.AddHours(2);
        var input = BuildInput(deliveryAt);
        var path = $"/v1/delivery-requests/{request.DeliveryRequestId}/promise";
        var initial = await SendAsync(client, path, "promise-decision-1", input);
        var initialJson = await initial.Content.ReadAsStringAsync();
        verifier.IsAvailable = false;
        var replay = await SendAsync(client, path, "promise-decision-1", input);
        var replayJson = await replay.Content.ReadAsStringAsync();
        var tamperedInput = input with
        {
            RequestedCriteria = input.RequestedCriteria!
                .Select((criterion, index) => index == 0
                    ? criterion with { ReasonCodes = ["TAMPERED_REASON"] }
                    : criterion)
                .ToArray()
        };
        var invalidReplay = await SendAsync(client, path, "promise-decision-1", tamperedInput);
        var invalidReplayJson = await invalidReplay.Content.ReadAsStringAsync();
        var changedInput = BuildInput(deliveryAt.AddHours(1));
        var conflict = await SendAsync(client, path, "promise-decision-1", changedInput);
        var conflictJson = await conflict.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var initialDocument = JsonDocument.Parse(initialJson);
        using var replayDocument = JsonDocument.Parse(replayJson);
        var promiseId = initialDocument.RootElement.GetProperty("promise_id").GetString()
            ?? throw new InvalidOperationException("The Promise response did not contain a promise_id.");
        Assert.Equal(promiseId, replayDocument.RootElement.GetProperty("promise_id").GetString());
        Assert.Equal(1, replayDocument.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("ACCEPTED", replayDocument.RootElement.GetProperty("outcome").GetString());
        Assert.DoesNotContain("safety_buffer", initialJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("reason_codes", initialJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("policy_versions", initialJson, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidReplay.StatusCode);
        using var invalidReplayDocument = JsonDocument.Parse(invalidReplayJson);
        Assert.Equal("PROMISE_EVIDENCE_INVALID",
            invalidReplayDocument.RootElement.GetProperty("error").GetProperty("code").GetString());

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var conflictDocument = JsonDocument.Parse(conflictJson);
        Assert.Equal("PROMISE_CONFLICT",
            conflictDocument.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(8, verifier.CallCount);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var count = new NpgsqlCommand("""
            SELECT count(*)
            FROM promise_versions
            WHERE promise_id = @promise_id;
            """, connection);
        count.Parameters.AddWithValue("promise_id", promiseId);
        Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
    }

    private static PromiseCommitInput BuildInput(DateTimeOffset committedDeliveryAt)
    {
        var evaluatedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var requestedAt = committedDeliveryAt;
        var criteria = FeasibilityEvidenceGate.Required.Select(criterion =>
        {
            var result = new FeasibilityCriterionResult(
                criterion, FeasibilityCriterionStatus.Pass, []);
            var attached = FeasibilityEvidenceIntegrityContract.Attach(
                result,
                "evidence-" + criterion,
                evaluatedAt,
                "test-feasibility-source",
                "test-attestation");
            var integrity = attached.Integrity!;
            return new PromiseCriterionInput(
                criterion.ToString(),
                attached.Status.ToString(),
                attached.ReasonCodes,
                new PromiseEvidenceIntegrityInput(
                    integrity.EvidenceId,
                    integrity.ObservedAt,
                    integrity.PayloadSha256,
                    integrity.SourceId,
                    integrity.SourceAttestation));
        }).ToArray();

        return new PromiseCommitInput(
            criteria,
            requestedAt,
            null,
            new PromiseCommitProposalInput(
                "prm_11111111111111111111111111111111",
                1,
                evaluatedAt,
                committedDeliveryAt,
                "Accepted",
                TimeSpan.FromMinutes(5),
                "safety-buffer-v1",
                new Dictionary<string, string>
                {
                    ["feasibility"] = "v1",
                    ["safety_buffer"] = "v1"
                },
                ["REQUESTED_PROMISE_ACCEPTED"]));
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string path,
        string idempotencyKey,
        PromiseCommitInput input)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(input)
        };
        message.Headers.Add("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(message);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        NpgsqlDataSource dataSource,
        string customerId,
        IFeasibilityEvidenceSourceVerifier verifier) =>
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
                services.RemoveAll<IFeasibilityEvidenceSourceVerifier>();
                services.AddSingleton<IFeasibilityEvidenceSourceVerifier>(verifier);
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
        var assembly = typeof(PromiseCommitEndpointTests).Assembly;
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

    private sealed record FixedCustomerContext(string CustomerId) : ICustomerContext;

    private sealed class FixedCredentialStore(ApiKeyCredential credential) : IApiKeyCredentialStore
    {
        public Task<ApiKeyCredential?> FindAsync(string keyId, CancellationToken cancellationToken) =>
            Task.FromResult<ApiKeyCredential?>(keyId == credential.KeyId ? credential : null);
    }

    private sealed class VerifiedFeasibilitySource : IFeasibilityEvidenceSourceVerifier
    {
        public int CallCount { get; private set; }
        public bool IsAvailable { get; set; } = true;

        public Task<FeasibilityEvidenceSourceVerification> VerifyAsync(
            string customerId,
            FeasibilityCriterion criterion,
            FeasibilityEvidenceIntegrity integrity,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new FeasibilityEvidenceSourceVerification(
                IsAvailable
                    ? FeasibilityEvidenceSourceVerificationStatus.Verified
                    : FeasibilityEvidenceSourceVerificationStatus.Unverified,
                IsAvailable ? "TEST_VERIFIED" : "TEST_SOURCE_UNAVAILABLE"));
        }
    }
}
