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
        Assert.Null(snapshot.LastSuccessAt);
    }

    [Fact]
    public void Successful_batch_recovers_worker_after_a_failure()
    {
        var status = new WebhookDeliveryWorkerStatus(TimeProvider.System);
        status.MarkEnabled();
        status.RecordFailure();

        Assert.Equal("degraded", status.GetSnapshot().State);

        status.RecordSuccess(2);

        var snapshot = status.GetSnapshot();
        Assert.Equal("healthy", snapshot.State);
        Assert.Equal(2, snapshot.LastBatchClaimed);
        Assert.Equal(snapshot.LastSuccessAt, snapshot.LastAttemptAt);
    }
}
