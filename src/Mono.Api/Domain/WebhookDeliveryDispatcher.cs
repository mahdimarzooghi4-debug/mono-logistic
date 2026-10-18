using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mono.Api.Persistence;

namespace Mono.Api.Domain;

public sealed class WebhookDeliveryDispatcher(
    PostgresWebhookDeliveryRepository repository,
    IWebhookDeliveryTransport transport,
    WebhookSecretProtector secretProtector,
    TimeProvider timeProvider)
{
    public async Task<int> ProcessBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (!secretProtector.IsConfigured)
            return 0;

        var now = timeProvider.GetUtcNow();
        await repository.EnqueueAsync(WebhookEventCatalog.All, batchSize, now, cancellationToken);
        var claims = await repository.ClaimDueAsync(batchSize, now, TimeSpan.FromMinutes(2), cancellationToken);
        foreach (var claim in claims)
        {
            WebhookDeliveryResult result;
            byte[]? signingSecret = null;
            try
            {
                signingSecret = secretProtector.UnprotectBytes(claim.SecretCiphertext);
                result = await transport.SendAsync(claim, signingSecret, cancellationToken);
            }
            catch (Exception exception) when (exception is CryptographicException or FormatException or DecoderFallbackException)
            {
                result = new WebhookDeliveryResult(null, "SECRET_UNAVAILABLE");
            }
            finally
            {
                if (signingSecret is not null)
                    CryptographicOperations.ZeroMemory(signingSecret);
            }

            await repository.CompleteAttemptAsync(claim, result, timeProvider.GetUtcNow(), cancellationToken);
        }
        return claims.Count;
    }
}

public sealed record WebhookDeliveryWorkerSnapshot(
    [property: System.Text.Json.Serialization.JsonPropertyName("enabled")] bool Enabled,
    [property: System.Text.Json.Serialization.JsonPropertyName("state")] string State,
    [property: System.Text.Json.Serialization.JsonPropertyName("last_attempt_at")] DateTimeOffset? LastAttemptAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("last_success_at")] DateTimeOffset? LastSuccessAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("last_batch_claimed")] int? LastBatchClaimed);

/// <summary>In-process, non-sensitive status of the optional webhook delivery worker.</summary>
public sealed class WebhookDeliveryWorkerStatus(TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private bool _enabled;
    private bool _initialized;
    private bool _blocked;
    private bool? _lastAttemptSucceeded;
    private DateTimeOffset? _lastAttemptAt;
    private DateTimeOffset? _lastSuccessAt;
    private int? _lastBatchClaimed;

    public void MarkDisabled()
    {
        lock (_gate)
        {
            _enabled = false;
            _blocked = false;
            _initialized = true;
        }
    }

    public void MarkBlocked()
    {
        lock (_gate)
        {
            _enabled = true;
            _blocked = true;
            _initialized = true;
            _lastAttemptSucceeded = false;
            _lastAttemptAt = timeProvider.GetUtcNow();
            _lastBatchClaimed = null;
        }
    }

    public void MarkEnabled()
    {
        lock (_gate)
        {
            _enabled = true;
            _blocked = false;
            _initialized = true;
        }
    }

    public void RecordSuccess(int claimedCount)
    {
        lock (_gate)
        {
            _enabled = true;
            _blocked = false;
            _initialized = true;
            _lastAttemptSucceeded = true;
            _lastAttemptAt = timeProvider.GetUtcNow();
            _lastSuccessAt = _lastAttemptAt;
            _lastBatchClaimed = claimedCount;
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            _enabled = true;
            _blocked = false;
            _initialized = true;
            _lastAttemptSucceeded = false;
            _lastAttemptAt = timeProvider.GetUtcNow();
            _lastBatchClaimed = null;
        }
    }

    public WebhookDeliveryWorkerSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var state = !_initialized ? "starting"
                : !_enabled ? "disabled"
                : _blocked ? "blocked"
                : _lastAttemptSucceeded is false ? "degraded"
                : _lastAttemptSucceeded is true ? "healthy"
                : "starting";
            return new WebhookDeliveryWorkerSnapshot(
                _enabled, state, _lastAttemptAt, _lastSuccessAt, _lastBatchClaimed);
        }
    }
}

public sealed class WebhookDeliveryBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    WebhookSecretProtector secretProtector,
    WebhookDeliveryWorkerStatus workerStatus,
    ILogger<WebhookDeliveryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Mono:WebhookDelivery:Enabled"))
        {
            workerStatus.MarkDisabled();
            logger.LogInformation("Webhook delivery worker is disabled by configuration.");
            return;
        }
        if (!secretProtector.IsConfigured)
        {
            workerStatus.MarkBlocked();
            logger.LogError("Webhook delivery worker remains disabled because secret protection is not configured.");
            return;
        }

        var pollSeconds = configuration.GetValue("Mono:WebhookDelivery:PollIntervalSeconds", 10);
        var batchSize = configuration.GetValue("Mono:WebhookDelivery:BatchSize", 20);
        if (pollSeconds is < 1 or > 300 || batchSize is < 1 or > 100)
            throw new InvalidOperationException("Webhook delivery polling settings are outside their supported bounds.");

        workerStatus.MarkEnabled();

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<WebhookDeliveryDispatcher>();
                processed = await dispatcher.ProcessBatchAsync(batchSize, stoppingToken);
                workerStatus.RecordSuccess(processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                workerStatus.RecordFailure();
                logger.LogError(exception, "Webhook delivery batch failed; leased deliveries will be recovered after their lease expires.");
            }

            if (processed == 0)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(pollSeconds), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }
}
