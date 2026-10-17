using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Mono.Api.Domain;
using Npgsql;
using Xunit;

namespace Mono.Api.Tests;

public sealed class MissionCourierOfferExpiryBackgroundServiceTests
{
    [Fact]
    public async Task Worker_is_disabled_when_configuration_is_missing()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var worker = new MissionCourierOfferExpiryBackgroundService(
            services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            NullLogger<MissionCourierOfferExpiryBackgroundService>.Instance);

        await worker.StartAsync(CancellationToken.None);
        await worker.ExecuteTask!;
        await worker.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Dispatcher_rejects_batch_sizes_outside_the_bound(int batchSize)
    {
        await using var dataSource = NpgsqlDataSource.Create(
            "Host=127.0.0.1;Port=1;Database=mono;Username=mono;Password=mono;Timeout=1");
        var dispatcher = new PostgresMissionCourierOfferExpiryDispatcher(dataSource, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => dispatcher.ExpireOverdueBatchAsync(batchSize, CancellationToken.None));
    }
}
