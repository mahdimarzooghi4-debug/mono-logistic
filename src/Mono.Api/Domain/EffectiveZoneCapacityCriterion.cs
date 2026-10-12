namespace Mono.Api.Domain;

/// <summary>
/// Compares an explicit effective-courier requirement with a zone capacity snapshot.
/// This criterion covers aggregate zone availability only, not a courier's shipment fit.
/// </summary>
public static class EffectiveZoneCapacityCriterion
{
    public static FeasibilityCriterionResult Evaluate(
        ZoneCapacitySnapshot? snapshot,
        int? requiredEffectiveCourierCount)
    {
        if (snapshot is null || requiredEffectiveCourierCount is null)
        {
            var reasons = new List<string>();
            if (snapshot is null)
                reasons.Add("ZONE_CAPACITY_SNAPSHOT_MISSING");
            if (requiredEffectiveCourierCount is null)
                reasons.Add("REQUIRED_EFFECTIVE_CAPACITY_UNAVAILABLE");

            return new FeasibilityCriterionResult(
                FeasibilityCriterion.Capacity,
                FeasibilityCriterionStatus.Unresolved,
                reasons);
        }

        if (requiredEffectiveCourierCount.Value <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(requiredEffectiveCourierCount),
                "Required effective courier count must be a positive integer.");

        var normalized = snapshot.Normalize();
        if (string.IsNullOrWhiteSpace(normalized.ZoneId) ||
            normalized.ObservedAt == default ||
            normalized.RegisteredCount < 0 ||
            normalized.OnlineCount < 0 ||
            normalized.EffectiveCount < 0)
        {
            throw new ArgumentException("Zone capacity snapshot is invalid.", nameof(snapshot));
        }

        if (normalized.EffectiveCount < requiredEffectiveCourierCount.Value)
        {
            return new FeasibilityCriterionResult(
                FeasibilityCriterion.Capacity,
                FeasibilityCriterionStatus.Fail,
                ["EFFECTIVE_CAPACITY_INSUFFICIENT"]);
        }

        return new FeasibilityCriterionResult(
            FeasibilityCriterion.Capacity,
            FeasibilityCriterionStatus.Pass,
            []);
    }
}
