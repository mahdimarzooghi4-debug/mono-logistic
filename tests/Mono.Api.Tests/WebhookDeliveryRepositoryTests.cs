using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class WebhookDeliveryRepositoryTests
{
    [Fact]
    public async Task Operations_queue_summary_is_role_checked_and_audited_immutably()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var actorId = "ops_" + Guid.NewGuid().ToString("N");
        var historyRepository = new PostgresWebhookDeliveryHistoryRepository(dataSource);
        var service = new PostgresWebhookOperationsReadService(dataSource,
            new TestOperationsContext(actorId, [MonoOperationsRoles.Administrator]), historyRepository);
        var summary = await service.GetQueueSummaryAsync(DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Equal(summary.TotalCount,
            summary.PendingCount + summary.InProgressCount + summary.RetryingCount + summary.SucceededCount +
            summary.DeadLetteredCount + summary.CancelledCount);
        Assert.True(summary.DueCount <= summary.PendingCount + summary.RetryingCount);
        Assert.True(summary.StaleLeaseCount <= summary.InProgressCount);

        await using var connection = await dataSource.OpenConnectionAsync();
        await using var audit = new NpgsqlCommand("""
            SELECT action, result_count FROM webhook_operations_read_audit WHERE actor_id = @actor_id;
            """, connection);
        audit.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        await using (var reader = await audit.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.Equal("QUEUE_SUMMARY", reader.GetString(0));
            Assert.Equal(summary.TotalCount, reader.GetInt64(1));
            Assert.False(await reader.ReadAsync());
        }

        var page = await service.SearchDeliveriesAsync(10, null, null, null, null,
            null, null, DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.True(page.Items.Count <= 10);
        var missingId = "whd_" + Guid.NewGuid().ToString("N");
        Assert.Null(await service.GetDeliveryDetailAsync(missingId, DateTimeOffset.UtcNow, CancellationToken.None));
        await using (var auditedReads = new NpgsqlCommand("""
            SELECT action, result_count, resource_id FROM webhook_operations_read_audit
            WHERE actor_id = @actor_id ORDER BY occurred_at, audit_id;
            """, connection))
        {
            auditedReads.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            await using var reader = await auditedReads.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("QUEUE_SUMMARY", reader.GetString(0));
            Assert.True(await reader.ReadAsync());
            Assert.Equal("QUEUE_SEARCH", reader.GetString(0));
            Assert.True(reader.IsDBNull(2));
            Assert.True(await reader.ReadAsync());
            Assert.Equal("DELIVERY_DETAIL", reader.GetString(0));
            Assert.Equal(missingId, reader.GetString(2));
            Assert.False(await reader.ReadAsync());
        }

        await using var mutate = new NpgsqlCommand("""
            UPDATE webhook_operations_read_audit SET action = 'QUEUE_SUMMARY' WHERE actor_id = @actor_id;
            """, connection);
        mutate.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        await Assert.ThrowsAsync<PostgresException>(() => mutate.ExecuteNonQueryAsync());

        var denied = new PostgresWebhookOperationsReadService(dataSource,
            new TestOperationsContext(actorId, []), historyRepository);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            denied.GetQueueSummaryAsync(DateTimeOffset.UtcNow, CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => denied.SearchDeliveriesAsync(10,
            null, null, null, null, null, null, DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task Outbox_fanout_is_idempotent_payloads_are_redacted_and_retry_history_is_append_only()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await ApplyMigrationsAsync(dataSource);
        var customer = Customer.Create("Webhook Delivery Test", "ACTIVE");
        await InsertCustomerAsync(dataSource, customer);
        var now = DateTimeOffset.UtcNow;
        const string endpointId = "whk_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        var protector = new WebhookSecretProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        var secret = protector.Protect("whsec_delivery-test");

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await using var endpoint = new NpgsqlCommand("""
                INSERT INTO webhook_endpoints (endpoint_id, customer_id, url, events, secret_ciphertext, status, created_at)
                VALUES (@endpoint_id, @customer_id, 'https://hooks.customer.com/mono',
                        ARRAY['promise.committed']::text[], @secret, 'ACTIVE', @created_at);
                """, connection);
            endpoint.Parameters.AddWithValue("endpoint_id", NpgsqlDbType.Text, endpointId);
            endpoint.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customer.CustomerId);
            endpoint.Parameters.AddWithValue("secret", NpgsqlDbType.Text, secret);
            endpoint.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now.AddMinutes(-1));
            await endpoint.ExecuteNonQueryAsync();

            await using var outbox = new NpgsqlCommand("""
                INSERT INTO outbox_events (
                    event_id, event_type, event_version, occurred_at, producer, correlation_id,
                    customer_id, aggregate_type, aggregate_id, sequence, data, created_at)
                VALUES (
                    @event_id, 'promise.committed', 1, @occurred_at, 'mono-api', 'delivery-test',
                    @customer_id, 'promise', 'prm_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 1, @data, @created_at);
                """, connection);
            outbox.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            outbox.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
            outbox.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customer.CustomerId);
            outbox.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb,
                """{"promise_id":"prm_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","delivery_request_id":"drq_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","version":1,"decision_id":"private-decision","outcome":"ACCEPTED","actor_id":"private-actor","reason_codes":["INTERNAL_ONLY"]}""");
            outbox.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            await outbox.ExecuteNonQueryAsync();
        }

        var repository = new PostgresWebhookDeliveryRepository(dataSource);
        Assert.Equal(1, await repository.EnqueueAsync(WebhookEventCatalog.All, 20, now, CancellationToken.None));
        Assert.Equal(0, await repository.EnqueueAsync(WebhookEventCatalog.All, 20, now, CancellationToken.None));

        var firstClaims = await repository.ClaimDueAsync(20, now, TimeSpan.FromMinutes(2), CancellationToken.None);
        var first = Assert.Single(firstClaims);
        Assert.Equal(1, first.AttemptNumber);
        using (var payload = JsonDocument.Parse(first.Payload))
        {
            var json = first.Payload;
            Assert.Equal("promise.committed", payload.RootElement.GetProperty("event_type").GetString());
            Assert.Equal("ACCEPTED", payload.RootElement.GetProperty("data").GetProperty("outcome").GetString());
            Assert.DoesNotContain("decision_id", json, StringComparison.Ordinal);
            Assert.DoesNotContain("actor_id", json, StringComparison.Ordinal);
            Assert.DoesNotContain("reason_codes", json, StringComparison.Ordinal);
        }

        await repository.CompleteAttemptAsync(first, new WebhookDeliveryResult(503, null), now, CancellationToken.None);
        Assert.Empty(await repository.ClaimDueAsync(20, now.AddSeconds(29), TimeSpan.FromMinutes(2), CancellationToken.None));
        var second = Assert.Single(await repository.ClaimDueAsync(
            20, now.AddSeconds(30), TimeSpan.FromMinutes(2), CancellationToken.None));
        Assert.Equal(2, second.AttemptNumber);
        await repository.CompleteAttemptAsync(second, new WebhookDeliveryResult((int)HttpStatusCode.OK, null),
            now.AddSeconds(30), CancellationToken.None);

        var historyRepository = new PostgresWebhookDeliveryHistoryRepository(dataSource);
        var history = await historyRepository.ListAsync(customer.CustomerId, 1, endpointId,
            "promise.committed", "SUCCEEDED", null, null, CancellationToken.None);
        var historyItem = Assert.Single(history.Items);
        Assert.Equal(first.DeliveryId, historyItem.DeliveryId);
        Assert.Null(history.NextCursor);
        var detail = await historyRepository.FindAsync(customer.CustomerId, first.DeliveryId, CancellationToken.None);
        Assert.NotNull(detail);
        Assert.Equal(new[] { "STARTED", "RETRY_SCHEDULED", "STARTED", "SUCCEEDED" },
            detail.Attempts.Select(attempt => attempt.Action));
        var operationsService = new PostgresWebhookOperationsReadService(dataSource,
            new TestOperationsContext("ops_" + Guid.NewGuid().ToString("N"), [MonoOperationsRoles.Administrator]),
            historyRepository);
        var operationsPage = await operationsService.SearchDeliveriesAsync(10, customer.CustomerId, endpointId,
            "promise.committed", "SUCCEEDED", null, null, now.AddSeconds(31), CancellationToken.None);
        Assert.Contains(operationsPage.Items, item => item.DeliveryId == first.DeliveryId);
        var operationsDetail = await operationsService.GetDeliveryDetailAsync(first.DeliveryId,
            now.AddSeconds(32), CancellationToken.None);
        Assert.NotNull(operationsDetail);
        Assert.Equal(detail.Attempts, operationsDetail.Attempts);
        Assert.Null(await historyRepository.FindAsync("cus_ffffffffffffffffffffffffffffffff", first.DeliveryId,
            CancellationToken.None));

        await using (var connection = await dataSource.OpenConnectionAsync())
        {
            await using var status = new NpgsqlCommand("""
                SELECT status, attempt_count FROM webhook_deliveries WHERE delivery_id = @delivery_id;
                """, connection);
            status.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, first.DeliveryId);
            await using var reader = await status.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("SUCCEEDED", reader.GetString(0));
            Assert.Equal(2, reader.GetInt32(1));
            await reader.CloseAsync();

            await using var attempts = new NpgsqlCommand("""
                SELECT array_agg(action ORDER BY attempt_event_id)
                FROM webhook_delivery_attempt_events WHERE delivery_id = @delivery_id;
                """, connection);
            attempts.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, first.DeliveryId);
            Assert.Equal(new[] { "STARTED", "RETRY_SCHEDULED", "STARTED", "SUCCEEDED" },
                await attempts.ExecuteScalarAsync() as string[]);

            await using var mutate = new NpgsqlCommand("""
                UPDATE webhook_delivery_attempt_events SET action = 'STARTED'
                WHERE delivery_id = @delivery_id;
                """, connection);
            mutate.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, first.DeliveryId);
            await Assert.ThrowsAsync<PostgresException>(() => mutate.ExecuteNonQueryAsync());

            await using var markDeadLetter = new NpgsqlCommand("""
                UPDATE webhook_deliveries SET status = 'DEAD_LETTERED'
                WHERE delivery_id = @delivery_id;
                """, connection);
            markDeadLetter.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, first.DeliveryId);
            await markDeadLetter.ExecuteNonQueryAsync();
        }

        var operationsReplayService = new PostgresWebhookOperationsReadService(dataSource,
            new TestOperationsContext("ops_" + Guid.NewGuid().ToString("N"), [MonoOperationsRoles.Administrator]),
            historyRepository);
        var operationsReplay = await operationsReplayService.ReplayDeliveryAsync(first.DeliveryId,
            "operations-retry-1", now.AddMinutes(1), CancellationToken.None);
        Assert.Null(operationsReplay.ErrorCode);
        Assert.Equal(1, operationsReplay.Delivery!.ReplayGeneration);
        var duplicateOperationsReplay = await operationsReplayService.ReplayDeliveryAsync(first.DeliveryId,
            "operations-retry-1", now.AddMinutes(2), CancellationToken.None);
        Assert.True(duplicateOperationsReplay.AlreadyRequested);
        Assert.Equal(operationsReplay.Delivery.DeliveryId, duplicateOperationsReplay.Delivery!.DeliveryId);
        await using (var replayAuditConnection = await dataSource.OpenConnectionAsync())
        await using (var replayAudit = new NpgsqlCommand("""
            SELECT outcome_code, result_count FROM webhook_operations_read_audit
            WHERE action = 'DELIVERY_REPLAY' AND resource_id = @delivery_id
            ORDER BY occurred_at, audit_id;
            """, replayAuditConnection))
        {
            replayAudit.Parameters.AddWithValue("delivery_id", NpgsqlDbType.Text, first.DeliveryId);
            await using var reader = await replayAudit.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("QUEUED", reader.GetString(0));
            Assert.Equal(1, reader.GetInt64(1));
            Assert.True(await reader.ReadAsync());
            Assert.Equal("ALREADY_REQUESTED", reader.GetString(0));
            Assert.Equal(1, reader.GetInt64(1));
            Assert.False(await reader.ReadAsync());
        }

        var firstReplay = await historyRepository.ReplayAsync(customer.CustomerId, first.DeliveryId,
            "retry-request-1", now.AddMinutes(1), CancellationToken.None);
        Assert.Null(firstReplay.ErrorCode);
        Assert.False(firstReplay.AlreadyRequested);
        Assert.Equal(2, firstReplay.Delivery!.ReplayGeneration);
        Assert.Equal("PENDING", firstReplay.Delivery.Status);
        var idempotentReplay = await historyRepository.ReplayAsync(customer.CustomerId, first.DeliveryId,
            "retry-request-1", now.AddMinutes(2), CancellationToken.None);
        Assert.True(idempotentReplay.AlreadyRequested);
        Assert.Equal(firstReplay.Delivery.DeliveryId, idempotentReplay.Delivery!.DeliveryId);
        var secondReplay = await historyRepository.ReplayAsync(customer.CustomerId, first.DeliveryId,
            "retry-request-2", now.AddMinutes(3), CancellationToken.None);
        var thirdReplay = await historyRepository.ReplayAsync(customer.CustomerId, first.DeliveryId,
            "retry-request-3", now.AddMinutes(4), CancellationToken.None);
        Assert.Equal(3, secondReplay.Delivery!.ReplayGeneration);
        Assert.Equal("REPLAY_LIMIT_REACHED", thirdReplay.ErrorCode);
        var beyondLimit = await historyRepository.ReplayAsync(customer.CustomerId, first.DeliveryId,
            "retry-request-4", now.AddMinutes(5), CancellationToken.None);
        Assert.Equal("REPLAY_LIMIT_REACHED", beyondLimit.ErrorCode);
        Assert.Null(await historyRepository.FindAsync("cus_ffffffffffffffffffffffffffffffff", first.DeliveryId,
            CancellationToken.None));
        await ApplyMigrationsAsync(dataSource);
    }

    [Fact]
    public void Webhook_signature_is_sha256_hmac_over_exact_payload_bytes()
    {
        var secret = Encoding.UTF8.GetBytes("whsec_test");
        var payload = Encoding.UTF8.GetBytes("{\"event_id\":\"evt_test\"}");
        var expected = "sha256=" + Convert.ToHexString(HMACSHA256.HashData(secret, payload)).ToLowerInvariant();
        Assert.Equal(expected, WebhookDeliverySignature.Compute(secret, payload));
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
        var assembly = typeof(WebhookDeliveryRepositoryTests).Assembly;
        foreach (var resourceName in new[]
                 {
                     "Mono.Api.Tests.Migrations.001_customer_branch.sql",
                     "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
                     "Mono.Api.Tests.Migrations.018_promise_commit.sql",
                     "Mono.Api.Tests.Migrations.054_webhook_endpoint_registry.sql",
                     "Mono.Api.Tests.Migrations.055_webhook_endpoint_lifecycle_audit.sql",
                     "Mono.Api.Tests.Migrations.056_webhook_delivery_queue.sql",
                     "Mono.Api.Tests.Migrations.057_webhook_delivery_history_index.sql",
                     "Mono.Api.Tests.Migrations.058_webhook_delivery_replay.sql",
                     "Mono.Api.Tests.Migrations.059_webhook_operations_read_audit.sql",
                     "Mono.Api.Tests.Migrations.060_webhook_operations_delivery_search.sql",
                     "Mono.Api.Tests.Migrations.061_webhook_operations_delivery_replay.sql"
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

    private sealed class TestOperationsContext(string actorId, IReadOnlyCollection<string> roles)
        : IMonoOperationsContext
    {
        public string ActorId => actorId;
        public IReadOnlyCollection<string> Roles => roles;
    }
}
