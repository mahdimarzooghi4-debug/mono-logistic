using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record CourierAvailabilityWindow(
    [property: JsonPropertyName("window_id")] string WindowId,
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("zone_id")] string ZoneId,
    [property: JsonPropertyName("starts_at")] DateTimeOffset StartsAt,
    [property: JsonPropertyName("ends_at")] DateTimeOffset EndsAt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("cancelled_by")] string? CancelledBy,
    [property: JsonPropertyName("cancelled_at")] DateTimeOffset? CancelledAt,
    [property: JsonPropertyName("cancellation_reason")] string? CancellationReason);

public sealed record CourierAvailabilityWindowPage(
    [property: JsonPropertyName("items")] IReadOnlyList<CourierAvailabilityWindow> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public enum CourierAvailabilityWriteOutcome
{
    Applied,
    Replayed,
    Conflict,
    CourierNotFound,
    InvalidCourierState,
    InvalidZone,
    Overlap,
    WindowNotFound
}

public sealed record CourierAvailabilityWriteResult(
    CourierAvailabilityWriteOutcome Outcome,
    CourierAvailabilityWindow? Window);
