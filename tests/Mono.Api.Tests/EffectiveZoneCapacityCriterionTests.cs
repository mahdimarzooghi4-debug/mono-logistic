using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class EffectiveZoneCapacityCriterionTests
{
    [Fact]
    public void EffectiveCapacityMeetingRequirementPasses()
    {
        var result = EffectiveZoneCapacityCriterion.Evaluate(Snapshot(effective: 4), 4);

        Assert.Equal(FeasibilityCriterion.Capacity, result.Criterion);
        Assert.Equal(FeasibilityCriterionStatus.Pass, result.Status);
        Assert.Empty(result.ReasonCodes);
    }

    [Fact]
    public void EffectiveCapacityAboveRequirementPasses()
    {
        var result = EffectiveZoneCapacityCriterion.Evaluate(Snapshot(effective: 8), 3);

        Assert.Equal(FeasibilityCriterionStatus.Pass, result.Status);
    }

    [Fact]
    public void EffectiveCapacityBelowRequirementFails()
    {
        var result = EffectiveZoneCapacityCriterion.Evaluate(Snapshot(effective: 2), 3);

        Assert.Equal(FeasibilityCriterionStatus.Fail, result.Status);
        Assert.Equal(new[] { "EFFECTIVE_CAPACITY_INSUFFICIENT" }, result.ReasonCodes);
    }

    [Fact]
    public void MissingSnapshotIsUnresolved()
    {
        var result = EffectiveZoneCapacityCriterion.Evaluate(null, 1);

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "ZONE_CAPACITY_SNAPSHOT_MISSING" }, result.ReasonCodes);
    }

    [Fact]
    public void MissingRequirementIsUnresolved()
    {
        var result = EffectiveZoneCapacityCriterion.Evaluate(Snapshot(effective: 4), null);

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "REQUIRED_EFFECTIVE_CAPACITY_UNAVAILABLE" }, result.ReasonCodes);
    }

    [Fact]
    public void BothMissingInputsReturnBothReasons()
    {
        var result = EffectiveZoneCapacityCriterion.Evaluate(null, null);

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(
            new[] { "ZONE_CAPACITY_SNAPSHOT_MISSING", "REQUIRED_EFFECTIVE_CAPACITY_UNAVAILABLE" },
            result.ReasonCodes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RequirementMustBePositive(int required)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EffectiveZoneCapacityCriterion.Evaluate(Snapshot(effective: 4), required));
    }

    [Fact]
    public void NegativeSnapshotCountsAreRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            EffectiveZoneCapacityCriterion.Evaluate(Snapshot(effective: -1), 1));
    }

    [Fact]
    public void PassingCapacityAloneDoesNotReadyTheAggregateGate()
    {
        var capacity = EffectiveZoneCapacityCriterion.Evaluate(Snapshot(effective: 4), 1);
        var gate = FeasibilityEvidenceGate.Evaluate([capacity]);

        Assert.Equal(FeasibilityGateStatus.Unresolved, gate.Status);
        Assert.Contains(FeasibilityCriterion.Coverage, gate.MissingCriteria);
    }

    private static ZoneCapacitySnapshot Snapshot(int effective) => new()
    {
        ZoneId = "zone-1",
        ObservedAt = DateTimeOffset.UnixEpoch,
        RegisteredCount = 5,
        OnlineCount = 4,
        EffectiveCount = effective
    };
}
