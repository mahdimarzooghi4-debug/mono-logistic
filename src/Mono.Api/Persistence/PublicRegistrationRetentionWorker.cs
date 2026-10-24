using Npgsql;

namespace Mono.Api.Persistence;

public sealed class PublicRegistrationRetentionWorker(
    NpgsqlDataSource dataSource,
    ILogger<PublicRegistrationRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(stoppingToken);
                await using var command = new NpgsqlCommand(
                    "DELETE FROM public_registration_requests WHERE expires_at <= now()", connection);
                var deleted = await command.ExecuteNonQueryAsync(stoppingToken);
                if (deleted > 0) logger.LogInformation("Expired {Count} public registration requests.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Public registration retention cleanup failed.");
            }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}