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
    }

    [Fact]
    public void Successful_batch_recovers_status_after_a_failed_batch()
    {
        var status = new MissionCourierOfferExpiryWorkerStatus(TimeProvider.System);
        status.MarkEnabled();
        status.RecordFailure();

        Assert.Equal("degraded", status.GetSnapshot().State);

        status.RecordSuccess(3);

        var snapshot = status.GetSnapshot();
        Assert.True(snapshot.Enabled);
        Assert.Equal("healthy", snapshot.State);
        Assert.Equal(3, snapshot.LastBatchCandidates);
        Assert.NotNull(snapshot.LastSuccessAt);
        Assert.Equal(snapshot.LastSuccessAt, snapshot.LastAttemptAt);
    }
}
