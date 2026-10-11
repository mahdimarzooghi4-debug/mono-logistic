using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class HanaDistancePricingV1Tests
{
    [Theory]
    [InlineData(0, 0, 20_000)]
    [InlineData(999.99, 0, 20_000)]
    [InlineData(1_000, 1_000, 27_500)]
    [InlineData(1_999.99, 1_000, 27_500)]
    [InlineData(2_000, 2_000, 35_000)]
    [InlineData(3_000, 3_000, 42_500)]
    [InlineData(4_000, 4_000, 50_000)]
    [InlineData(5_000, 4_000, 50_000)]
    public void Uses_lower_inclusive_distance_bands_and_approved_fares(
        decimal distanceMeters,
        int expectedBandStartMeters,
        long expectedFareToman)
    {
        var result = HanaDistancePricingV1.Calculate(distanceMeters);

        Assert.Equal(HanaDistancePricingV1.PolicyVersion, result.PolicyVersion);
        Assert.Equal(distanceMeters, result.DistanceMeters);
        Assert.Equal(expectedBandStartMeters, result.DistanceBandStartMeters);
        Assert.Equal(expectedFareToman, result.FareToman);
    }

    [Theory]
    [InlineData(20_000, 10_000)]
    [InlineData(27_500, 13_750)]
    [InlineData(35_000, 17_500)]
    [InlineData(42_500, 21_250)]
    [InlineData(50_000, 25_000)]
    public void Splits_each_approved_fare_equally_between_buyer_and_seller(
        long fareToman,
        long expectedShareToman)
    {
        var result = HanaDistancePricingV1.Calculate(
            fareToman switch
            {
                20_000 => 500,
                27_500 => 1_500,
                35_000 => 2_500,
                42_500 => 3_500,
                _ => 4_500
            });

        Assert.Equal(fareToman, result.FareToman);
        Assert.Equal(expectedShareToman, result.BuyerShareToman);
        Assert.Equal(expectedShareToman, result.SellerShareToman);
        Assert.Equal(fareToman, result.BuyerShareToman + result.SellerShareToman);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(5_000.01)]
    public void Rejects_distance_outside_hana_phase_one_range(decimal distanceMeters) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            HanaDistancePricingV1.Calculate(distanceMeters));
}
