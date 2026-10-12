namespace Mono.Api.Domain;

public sealed record HanaDistanceFare(
    string PolicyVersion,
    decimal DistanceMeters,
    int DistanceBandStartMeters,
    long FareToman,
    long BuyerShareToman,
    long SellerShareToman);

/// <summary>
/// Applies Hana Phase-1's approved distance tariff only.
/// No courier mode, weight, zone, tax, commission, or settlement rule is inferred here.
/// </summary>
public static class HanaDistancePricingV1
{
    public const string PolicyVersion = "hana-distance-v1";
    public const int MaximumDistanceMeters = 5_000;

    public static HanaDistanceFare Calculate(decimal distanceMeters)
    {
        if (distanceMeters < 0 || distanceMeters > MaximumDistanceMeters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(distanceMeters),
                $"Distance must be between 0 and {MaximumDistanceMeters} metres.");
        }

        var (bandStartMeters, fareToman) = distanceMeters switch
        {
            < 1_000m => (0, 20_000L),
            < 2_000m => (1_000, 27_500L),
            < 3_000m => (2_000, 35_000L),
            < 4_000m => (3_000, 42_500L),
            _ => (4_000, 50_000L)
        };

        var buyerShareToman = fareToman / 2;
        return new HanaDistanceFare(
            PolicyVersion,
            distanceMeters,
            bandStartMeters,
            fareToman,
            buyerShareToman,
            fareToman - buyerShareToman);
    }
}
