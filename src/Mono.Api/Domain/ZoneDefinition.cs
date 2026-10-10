using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public enum ZoneKind
{
    City,
    OperatingZone,
    MicroZone
}

/// <summary>
/// Geometry-neutral representation of a city, operating zone, or micro-zone.
/// Metadata identifiers remain opaque until their owning capabilities are implemented.
/// </summary>
public sealed record ZoneDefinition
{
    [JsonPropertyName("zone_id")]
    public string ZoneId { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public ZoneKind Kind { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("parent_zone_id")]
    public string? ParentZoneId { get; init; }

    [JsonPropertyName("geometry_reference")]
    public string? GeometryReference { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("capacity_policy_id")]
    public string? CapacityPolicyId { get; init; }

    public ZoneDefinition Normalize() => this with
    {
        ZoneId = ZoneId?.Trim() ?? string.Empty,
        Name = Name?.Trim() ?? string.Empty,
        ParentZoneId = NormalizeOptional(ParentZoneId),
        GeometryReference = NormalizeOptional(GeometryReference),
        Status = NormalizeOptional(Status),
        CapacityPolicyId = NormalizeOptional(CapacityPolicyId)
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class ZoneHierarchyValidator
{
    public static IReadOnlyList<string> Validate(IReadOnlyCollection<ZoneDefinition> zones)
    {
        ArgumentNullException.ThrowIfNull(zones);
        var errors = new List<string>();
        var normalizedZones = new List<ZoneDefinition>(zones.Count);
        var zonesById = new Dictionary<string, ZoneDefinition>(StringComparer.Ordinal);

        foreach (var zone in zones)
        {
            if (zone is null)
            {
                errors.Add("Zone collection cannot contain null entries.");
                continue;
            }

            var normalized = zone.Normalize();
            normalizedZones.Add(normalized);

            if (string.IsNullOrWhiteSpace(normalized.ZoneId))
                errors.Add("zone_id is required.");

            if (string.IsNullOrWhiteSpace(normalized.Name))
                errors.Add("name is required.");

            if (!Enum.IsDefined(typeof(ZoneKind), normalized.Kind))
            {
                errors.Add("kind is not a supported zone kind.");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalized.ZoneId) &&
                !zonesById.TryAdd(normalized.ZoneId, normalized))
                errors.Add("zone_id values must be unique.");
        }

        foreach (var zone in normalizedZones)
        {
            if (!Enum.IsDefined(typeof(ZoneKind), zone.Kind))
                continue;

            var expectedParentKind = zone.Kind switch
            {
                ZoneKind.City => (ZoneKind?)null,
                ZoneKind.OperatingZone => ZoneKind.City,
                ZoneKind.MicroZone => ZoneKind.OperatingZone,
                _ => null
            };

            if (zone.Kind == ZoneKind.City)
            {
                if (zone.ParentZoneId is not null)
                    errors.Add("City zones cannot have a parent_zone_id.");
                continue;
            }

            if (zone.ParentZoneId is null)
            {
                errors.Add(zone.Kind == ZoneKind.OperatingZone
                    ? "Operating zones require a City parent."
                    : "Micro-zones require an Operating Zone parent.");
                continue;
            }

            if (!zonesById.TryGetValue(zone.ParentZoneId, out var parent))
            {
                errors.Add("parent_zone_id must refer to a zone in the same registry.");
                continue;
            }

            if (parent.Kind != expectedParentKind)
                errors.Add(zone.Kind == ZoneKind.OperatingZone
                    ? "Operating zone parent must be a City."
                    : "Micro-zone parent must be an Operating Zone.");
        }

        return errors;
    }
}
