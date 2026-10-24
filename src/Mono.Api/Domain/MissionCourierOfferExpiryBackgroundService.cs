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

public sealed record MissionCourierOfferExpiryWorkerSnapshot(
    [property: System.Text.Json.Serialization.JsonPropertyName("enabled")] bool Enabled,
    [property: System.Text.Json.Serialization.JsonPropertyName("state")] string State,
    [property: System.Text.Json.Serialization.JsonPropertyName("last_attempt_at")] DateTimeOffset? LastAttemptAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("last_success_at")] DateTimeOffset? LastSuccessAt,
    [property: System.Text.Json.Serialization.JsonPropertyName("last_batch_candidates")] int? LastBatchCandidates,
    [property: System.Text.Json.Serialization.JsonPropertyName("total_batches")] long TotalBatches,
    [property: System.Text.Json.Serialization.JsonPropertyName("successful_batches")] long SuccessfulBatches,
    [property: System.Text.Json.Serialization.JsonPropertyName("failed_batches")] long FailedBatches);

/// <summary>In-process, non-sensitive status of the optional offer expiry worker.</summary>
public sealed class MissionCourierOfferExpiryWorkerStatus(TimeProvider timeProvider)
{
    private readonly object _gate = new();
    private bool _enabled;
    private bool _initialized;
    private bool? _lastAttemptSucceeded;
    private DateTimeOffset? _lastAttemptAt;
    private DateTimeOffset? _lastSuccessAt;
    private int? _lastBatchCandidates;
    private long _totalBatches;
    private long _successfulBatches;
    private long _failedBatches;

    public void MarkDisabled()
    {
        lock (_gate)
        {
            _enabled = false;
            _initialized = true;
        }
    }

    public void MarkEnabled()
    {
        lock (_gate)
        {
            _enabled = true;
            _initialized = true;
        }
    }

    public void RecordSuccess(int candidateCount)
    {
        lock (_gate)
        {
            _enabled = true;
            _initialized = true;
            _lastAttemptSucceeded = true;
            _totalBatches++;
            _successfulBatches++;
            _lastAttemptAt = timeProvider.GetUtcNow();
            _lastSuccessAt = _lastAttemptAt;
            _lastBatchCandidates = candidateCount;
        }
    }

    public void RecordFailure()
    {
        lock (_gate)
        {
            _enabled = true;
            _initialized = true;
            _lastAttemptSucceeded = false;
            _totalBatches++;
            _failedBatches++;
            _lastAttemptAt = timeProvider.GetUtcNow();
            _lastBatchCandidates = null;
        }
    }

    public MissionCourierOfferExpiryWorkerSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            var state = !_initialized ? "starting"
                : !_enabled ? "disabled"
                : _lastAttemptSucceeded is false ? "degraded"
                : _lastAttemptSucceeded is true ? "healthy"
                : "starting";
            return new MissionCourierOfferExpiryWorkerSnapshot(
                _enabled, state, _lastAttemptAt, _lastSuccessAt, _lastBatchCandidates,
                _totalBatches, _successfulBatches, _failedBatches);
        }
    }
}

public sealed class MissionCourierOfferExpiryBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    MissionCourierOfferExpiryWorkerStatus workerStatus,
    ILogger<MissionCourierOfferExpiryBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue<bool>("Mono:MissionOfferExpiry:Enabled"))
        {
            workerStatus.MarkDisabled();
            logger.LogInformation("Mission courier offer expiry worker is disabled by configuration.");
            return;
        }

        workerStatus.MarkEnabled();
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
                workerStatus.RecordSuccess(processed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                workerStatus.RecordFailure();
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
