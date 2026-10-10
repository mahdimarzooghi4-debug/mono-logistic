using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class HanaMvpCapacityRuleTests
{
    [Fact]
    public void Calculates_registered_target_for_eight_active_stores()
    {
        Assert.Equal(80, HanaMvpCapacityRule.CalculateRegisteredTarget(8));
    }

    [Fact]
    public void Zero_active_stores_produces_zero_target()
    {
        Assert.Equal(0, HanaMvpCapacityRule.CalculateRegisteredTarget(0));
    }

    [Fact]
    public void Rejects_negative_active_store_count()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HanaMvpCapacityRule.CalculateRegisteredTarget(-1));
    }

    [Fact]
    public void Reports_target_as_shared_pool_capacity_not_per_store()
    {
        Assert.Equal(10, HanaMvpCapacityRule.CalculateRegisteredTarget(1));
        Assert.Equal(30, HanaMvpCapacityRule.CalculateRegisteredTarget(3));
    }

    [Fact]
    public void Detects_target_overflow()
    {
        Assert.Throws<OverflowException>(() =>
            HanaMvpCapacityRule.CalculateRegisteredTarget(long.MaxValue));
    }
}
