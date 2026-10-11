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
