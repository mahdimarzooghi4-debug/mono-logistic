using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

/// <summary>
/// A point-in-time report of the three separately tracked capacity figures for one zone.
/// </summary>
public sealed record ZoneCapacitySnapshot
{
    [JsonPropertyName("zone_id")]
    public string ZoneId { get; init; } = string.Empty;

    [JsonPropertyName("observed_at")]
    public DateTimeOffset ObservedAt { get; init; }

    [JsonPropertyName("registered_count")]
    public int RegisteredCount { get; init; }

    [JsonPropertyName("online_count")]
    public int OnlineCount { get; init; }

    [JsonPropertyName("effective_count")]
    public int EffectiveCount { get; init; }

    public ZoneCapacitySnapshot Normalize() => this with
    {
        ZoneId = ZoneId?.Trim() ?? string.Empty
    };
}

public static class ZoneCapacitySnapshotValidator
{
    public static IReadOnlyList<string> Validate(
        ZoneCapacitySnapshot snapshot,
        IReadOnlyCollection<ZoneDefinition> zoneRegistry)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(zoneRegistry);

        var errors = new List<string>();
        var normalized = snapshot.Normalize();

        if (string.IsNullOrWhiteSpace(normalized.ZoneId))
            errors.Add("zone_id is required.");

        if (normalized.ObservedAt == default)
            errors.Add("observed_at is required.");

        if (normalized.RegisteredCount < 0)
            errors.Add("registered_count cannot be negative.");

        if (normalized.OnlineCount < 0)
            errors.Add("online_count cannot be negative.");

        if (normalized.EffectiveCount < 0)
            errors.Add("effective_count cannot be negative.");

        errors.AddRange(ZoneHierarchyValidator.Validate(zoneRegistry)
            .Select(error => $"Zone registry: {error}"));

        var knownZoneIds = zoneRegistry
            .Where(zone => zone is not null)
            .Select(zone => zone.Normalize().ZoneId)
            .Where(zoneId => !string.IsNullOrWhiteSpace(zoneId))
            .ToHashSet(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(normalized.ZoneId) &&
            !knownZoneIds.Contains(normalized.ZoneId))
        {
            errors.Add("zone_id must refer to a zone in the registry.");
        }

        return errors;
    }
}
