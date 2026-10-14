using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class MissionFareReviewTests
{
    [Theory]
    [InlineData(20000, 10000, 10000, 2000, 200, 17800)]
    [InlineData(27500, 13750, 13750, 2750, 275, 24475)]
    [InlineData(50000, 25000, 25000, 5000, 500, 44500)]
    public void Allocation_estimate_uses_phase_one_documented_rates(
        int fare, int buyer, int seller, decimal commission, decimal vat, decimal courierNet)
    {
        var item = MissionFareReviewItem.Create(
            "mis_0123456789abcdef0123456789abcdef",
            "cus_0123456789abcdef0123456789abcdef",
            "drq_0123456789abcdef0123456789abcdef",
            "DELIVERED",
            DateTimeOffset.Parse("2026-10-14T10:00:00Z"),
            DateTimeOffset.Parse("2026-10-14T09:00:00Z"),
            "quo_phase1", 1, 1200m, fare, buyer, seller);

        Assert.Equal(commission, item.MonoCommissionEstimateToman);
        Assert.Equal(vat, item.CommissionVatEstimateToman);
        Assert.Equal(courierNet, item.CourierNetEstimateToman);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(10000, 4000, 4000)]
    [InlineData(10000, -1, 10001)]
    public void Allocation_rejects_invalid_or_unbalanced_recorded_fare(int fare, int buyer, int seller) =>
        Assert.Throws<ArgumentException>(() => MissionFareReviewItem.Create(
            "mis_0123456789abcdef0123456789abcdef",
            "cus_0123456789abcdef0123456789abcdef",
            "drq_0123456789abcdef0123456789abcdef",
            "PROMISED",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            "quo_phase1", 1, 1200m, fare, buyer, seller));
}
