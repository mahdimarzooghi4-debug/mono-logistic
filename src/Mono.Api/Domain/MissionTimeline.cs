using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record MissionTimelineEvent(
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record MissionTimelineResponse(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("events")] IReadOnlyList<MissionTimelineEvent> Events);

public sealed record DeliveryRequestTimelineEvent(
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("aggregate_type")] string AggregateType,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record DeliveryRequestTimelineResponse(
    [property: JsonPropertyName("delivery_request_id")] string DeliveryRequestId,
    [property: JsonPropertyName("events")] IReadOnlyList<DeliveryRequestTimelineEvent> Events);
