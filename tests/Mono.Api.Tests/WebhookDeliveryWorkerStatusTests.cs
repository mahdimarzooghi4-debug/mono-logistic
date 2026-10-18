using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class WebhookDeliveryWorkerStatusTests
{
    [Fact]
    public void Disabled_worker_is_reported_without_failure()
    {
        var status = new WebhookDeliveryWorkerStatus(TimeProvider.System);

        status.MarkDisabled();

        var snapshot = status.GetSnapshot();
        Assert.False(snapshot.Enabled);
        Assert.Equal("disabled", snapshot.State);
        Assert.Null(snapshot.LastAttemptAt);
        Assert.Equal(0L, snapshot.TotalBatches);
        Assert.Equal(0L, snapshot.SuccessfulBatches);
        Assert.Equal(0L, snapshot.FailedBatches);
    }

    [Fact]
    public void Missing_secret_protection_is_reported_as_blocked_without_secret_details()
    {
        var status = new WebhookDeliveryWorkerStatus(TimeProvider.System);

        status.MarkBlocked();

        var snapshot = status.GetSnapshot();
        Assert.True(snapshot.Enabled);
        Assert.Equal("blocked", snapshot.State);
        Assert.Null(snapshot.LastBatchClaimed);
        Assert.Equal(0L, snapshot.TotalBatches);
        Assert.Equal(0L, snapshot.FailedBatches);
        Assert.Null(snapshot.LastSuccessAt);
    }

    [Fact]
    public void Successful_batch_recovers_worker_after_a_failure()
    {
        var status = new WebhookDeliveryWorkerStatus(TimeProvider.System);
        status.MarkEnabled();
        status.RecordFailure();

        var degraded = status.GetSnapshot();
        Assert.Equal("degraded", degraded.State);
        Assert.Equal(1L, degraded.TotalBatches);
        Assert.Equal(1L, degraded.FailedBatches);

        status.RecordSuccess(2);

        var snapshot = status.GetSnapshot();
        Assert.Equal("healthy", snapshot.State);
        Assert.Equal(2, snapshot.LastBatchClaimed);
        Assert.Equal(2L, snapshot.TotalBatches);
        Assert.Equal(1L, snapshot.SuccessfulBatches);
        Assert.Equal(1L, snapshot.FailedBatches);
        Assert.Equal(snapshot.LastSuccessAt, snapshot.LastAttemptAt);
    }
}
