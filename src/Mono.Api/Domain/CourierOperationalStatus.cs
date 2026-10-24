using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record CourierOperationalStatusEvent(
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record CourierOperationalStatusSnapshot(
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("trust_status")] string TrustStatus,
    [property: JsonPropertyName("operational_status")] string OperationalStatus,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt,
    [property: JsonPropertyName("history")] IReadOnlyList<CourierOperationalStatusEvent> History);

public enum CourierOperationalStatusOutcome
{
    Applied,
    Replayed,
    Conflict,
    CourierNotFound,
    InvalidState
}

public sealed record CourierOperationalStatusResult(
    CourierOperationalStatusOutcome Outcome,
    CourierOperationalStatusSnapshot? Snapshot);
