using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class MissionCourierOfferExpiryWorkerStatusTests
{
    [Fact]
    public void Disabled_worker_is_reported_without_failure()
    {
        var status = new MissionCourierOfferExpiryWorkerStatus(TimeProvider.System);

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
    public void Successful_batch_recovers_status_after_a_failed_batch()
    {
        var status = new MissionCourierOfferExpiryWorkerStatus(TimeProvider.System);
        status.MarkEnabled();
        status.RecordFailure();

        var degraded = status.GetSnapshot();
        Assert.Equal("degraded", degraded.State);
        Assert.Equal(1L, degraded.TotalBatches);
        Assert.Equal(1L, degraded.FailedBatches);

        status.RecordSuccess(3);

        var snapshot = status.GetSnapshot();
        Assert.True(snapshot.Enabled);
        Assert.Equal("healthy", snapshot.State);
        Assert.Equal(3, snapshot.LastBatchCandidates);
        Assert.Equal(2L, snapshot.TotalBatches);
        Assert.Equal(1L, snapshot.SuccessfulBatches);
        Assert.Equal(1L, snapshot.FailedBatches);
        Assert.NotNull(snapshot.LastSuccessAt);
        Assert.Equal(snapshot.LastSuccessAt, snapshot.LastAttemptAt);
    }
}
