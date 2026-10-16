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

public sealed class WebhookDeliveryBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    WebhookSecretProtector secretProtector,
    ILogger<WebhookDeliveryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Mono:WebhookDelivery:Enabled"))
        {
            logger.LogInformation("Webhook delivery worker is disabled by configuration.");
            return;
        }
        if (!secretProtector.IsConfigured)
        {
            logger.LogError("Webhook delivery worker remains disabled because secret protection is not configured.");
            return;
        }

        var pollSeconds = configuration.GetValue("Mono:WebhookDelivery:PollIntervalSeconds", 10);
        var batchSize = configuration.GetValue("Mono:WebhookDelivery:BatchSize", 20);
        if (pollSeconds is < 1 or > 300 || batchSize is < 1 or > 100)
            throw new InvalidOperationException("Webhook delivery polling settings are outside their supported bounds.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<WebhookDeliveryDispatcher>();
                processed = await dispatcher.ProcessBatchAsync(batchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
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
