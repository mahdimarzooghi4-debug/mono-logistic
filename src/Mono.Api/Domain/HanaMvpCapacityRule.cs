namespace Mono.Api.Domain;

/// <summary>
/// Hana Phase 1 baseline for the registered courier capacity target.
/// The result belongs to the shared Zone courier pool, not to individual stores.
/// </summary>
public static class HanaMvpCapacityRule
{
    public const long CouriersPerActiveStore = 10;

    public static long CalculateRegisteredTarget(long activeHanaStoreCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(activeHanaStoreCount);
        return checked(activeHanaStoreCount * CouriersPerActiveStore);
    }
}
