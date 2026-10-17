using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Domain;

/// <summary>Expires due courier offers using the same transactional service as operations.</summary>
public sealed class PostgresMissionCourierOfferExpiryDispatcher(
    NpgsqlDataSource dataSource,
    TimeProvider timeProvider)
{
    public const int MaximumBatchSize = 100;
    private const string SystemActorId = "mono-dispatch-expiry-worker";

    public async Task<int> ExpireOverdueBatchAsync(int batchSize, CancellationToken cancellationToken)
    {
        if (batchSize is < 1 or > MaximumBatchSize)
            throw new ArgumentOutOfRangeException(nameof(batchSize));

        var now = timeProvider.GetUtcNow();
        const string sql = """
            SELECT offer.offer_id, offer.mission_id
            FROM mission_courier_offers offer
            JOIN missions mission ON mission.mission_id = offer.mission_id
            WHERE offer.status = 'OFFERED'
              AND offer.expires_at <= @now
              AND mission.status = 'SEARCHING_COURIER'
            ORDER BY offer.expires_at ASC, offer.offer_id ASC
            LIMIT @take;
            """;
        var candidates = new List<(string OfferId, string MissionId)>(batchSize);
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("now", NpgsqlDbType.TimestampTz, now);
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, batchSize);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                candidates.Add((reader.GetString(0), reader.GetString(1)));
        }

        var operationsContext = new SystemOperationsContext();
        var offerService = new PostgresMissionCourierOfferService(dataSource, operationsContext, timeProvider);
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await offerService.ExpireAsync(candidate.MissionId, candidate.OfferId,
                "worker-expire-" + candidate.OfferId, cancellationToken);
        }

        return candidates.Count;
    }

    private sealed class SystemOperationsContext : IMonoOperationsContext
    {
        public string ActorId => SystemActorId;
        public IReadOnlyCollection<string> Roles { get; } = [MonoOperationsRoles.Administrator];
    }
}

public sealed class MissionCourierOfferExpiryBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<MissionCourierOfferExpiryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Mono:MissionOfferExpiry:Enabled"))
        {
            logger.LogInformation("Mission courier offer expiry worker is disabled by configuration.");
            return;
        }

        var pollSeconds = configuration.GetValue("Mono:MissionOfferExpiry:PollIntervalSeconds", 10);
        var batchSize = configuration.GetValue("Mono:MissionOfferExpiry:BatchSize", 20);
        if (pollSeconds is < 1 or > 300 || batchSize is < 1 or > PostgresMissionCourierOfferExpiryDispatcher.MaximumBatchSize)
            throw new InvalidOperationException("Mission offer expiry polling settings are outside their supported bounds.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var processed = 0;
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<PostgresMissionCourierOfferExpiryDispatcher>();
                processed = await dispatcher.ExpireOverdueBatchAsync(batchSize, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Mission courier offer expiry batch failed.");
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
