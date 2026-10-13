using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record CourierPlanningEligibilityItem(
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("trust_status")] string TrustStatus,
    [property: JsonPropertyName("operational_status")] string OperationalStatus,
    [property: JsonPropertyName("zone_approved")] bool ZoneApproved,
    [property: JsonPropertyName("has_scheduled_window")] bool HasScheduledWindow,
    [property: JsonPropertyName("planned_capacity_contributor")] bool PlannedCapacityContributor,
    [property: JsonPropertyName("exclusion_reasons")] IReadOnlyList<string> ExclusionReasons,
    [property: JsonPropertyName("is_online")] bool IsOnline,
    [property: JsonPropertyName("live_presence_zone_id")] string? LivePresenceZoneId,
    [property: JsonPropertyName("live_presence_updated_at")] DateTimeOffset? LivePresenceUpdatedAt,
    [property: JsonPropertyName("live_presence_candidate")] bool LivePresenceCandidate,
    [property: JsonPropertyName("live_presence_exclusion_reasons")] IReadOnlyList<string> LivePresenceExclusionReasons);

public sealed record CourierPlanningEligibilityPage(
    [property: JsonPropertyName("zone_id")] string ZoneId,
    [property: JsonPropertyName("from")] DateTimeOffset From,
    [property: JsonPropertyName("to")] DateTimeOffset To,
    [property: JsonPropertyName("classification")] string Classification,
    [property: JsonPropertyName("items")] IReadOnlyList<CourierPlanningEligibilityItem> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);
