using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

/// <summary>
/// Structural record of a courier's requested and approved operating zones.
/// It does not evaluate geographic membership or authorize live operations.
/// </summary>
public sealed record CourierZoneApproval
{
    [JsonPropertyName("courier_id")]
    public string CourierId { get; init; } = string.Empty;

    [JsonPropertyName("preferred_zone_ids")]
    public IReadOnlyCollection<string> PreferredZoneIds { get; init; } = Array.Empty<string>();

    [JsonPropertyName("approved_zone_ids")]
    public IReadOnlyCollection<string> ApprovedZoneIds { get; init; } = Array.Empty<string>();

    [JsonPropertyName("primary_zone_id")]
    public string? PrimaryZoneId { get; init; }

    public CourierZoneApproval Normalize() => this with
    {
        CourierId = CourierId?.Trim() ?? string.Empty,
        PreferredZoneIds = NormalizeIds(PreferredZoneIds),
        ApprovedZoneIds = NormalizeIds(ApprovedZoneIds),
        PrimaryZoneId = NormalizeOptional(PrimaryZoneId)
    };

    private static IReadOnlyCollection<string> NormalizeIds(IReadOnlyCollection<string>? ids) =>
        ids?.Select(id => id?.Trim() ?? string.Empty).ToArray() ?? Array.Empty<string>();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class CourierZoneApprovalValidator
{
    public static IReadOnlyList<string> Validate(
        CourierZoneApproval approval,
        IReadOnlyCollection<ZoneDefinition> zoneRegistry)
    {
        ArgumentNullException.ThrowIfNull(approval);
        ArgumentNullException.ThrowIfNull(zoneRegistry);

        var errors = new List<string>();
        var normalized = approval.Normalize();

        if (string.IsNullOrWhiteSpace(normalized.CourierId))
            errors.Add("courier_id is required.");

        var registryErrors = ZoneHierarchyValidator.Validate(zoneRegistry);
        errors.AddRange(registryErrors.Select(error => $"Zone registry: {error}"));

        var knownZoneIds = zoneRegistry
            .Where(zone => zone is not null)
            .Select(zone => zone.Normalize().ZoneId)
            .Where(zoneId => !string.IsNullOrWhiteSpace(zoneId))
            .ToHashSet(StringComparer.Ordinal);

        var preferredZoneIds = ValidateZoneIds(
            normalized.PreferredZoneIds,
            "preferred_zone_ids",
            knownZoneIds,
            errors);

        var approvedZoneIds = ValidateZoneIds(
            normalized.ApprovedZoneIds,
            "approved_zone_ids",
            knownZoneIds,
            errors);

        foreach (var approvedZoneId in approvedZoneIds)
        {
            if (!preferredZoneIds.Contains(approvedZoneId))
                errors.Add("approved_zone_ids must be selected from preferred_zone_ids.");
        }

        if (normalized.PrimaryZoneId is not null)
        {
            if (!knownZoneIds.Contains(normalized.PrimaryZoneId))
                errors.Add("primary_zone_id must refer to a zone in the registry.");

            if (!approvedZoneIds.Contains(normalized.PrimaryZoneId))
                errors.Add("primary_zone_id must refer to an approved zone.");
        }

        return errors;
    }

    private static HashSet<string> ValidateZoneIds(
        IReadOnlyCollection<string> zoneIds,
        string fieldName,
        HashSet<string> knownZoneIds,
        ICollection<string> errors)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zoneId in zoneIds)
        {
            if (string.IsNullOrWhiteSpace(zoneId))
            {
                errors.Add($"{fieldName} cannot contain blank IDs.");
                continue;
            }

            if (!seen.Add(zoneId))
                errors.Add($"{fieldName} values must be unique.");

            if (!knownZoneIds.Contains(zoneId))
                errors.Add($"{fieldName} must refer to zones in the registry.");
        }

        return seen;
    }
}
