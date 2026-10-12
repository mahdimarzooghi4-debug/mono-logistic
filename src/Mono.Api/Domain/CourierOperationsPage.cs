using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record CourierOperationsSummary(
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("preferred_zone_ids")] IReadOnlyList<string> PreferredZoneIds,
    [property: JsonPropertyName("approved_zone_ids")] IReadOnlyList<string> ApprovedZoneIds,
    [property: JsonPropertyName("primary_zone_id")] string? PrimaryZoneId,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public sealed record CourierOperationsPage(
    [property: JsonPropertyName("items")] IReadOnlyList<CourierOperationsSummary> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);
