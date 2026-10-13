using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record MissionCourierOffer(
    [property: JsonPropertyName("offer_id")] string OfferId,
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("zone_id")] string ZoneId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public enum MissionCourierOfferOutcome
{
    Applied,
    Replayed,
    Conflict,
    MissionNotFound,
    CourierNotFound,
    OfferNotFound,
    CourierNotAvailable,
    InvalidState
}

public sealed record MissionCourierOfferResult(
    MissionCourierOfferOutcome Outcome,
    MissionCourierOffer? Offer,
    string? MissionStatus = null);

public sealed record MissionCourierOfferEvent(
    [property: JsonPropertyName("event_sequence")] long EventSequence,
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("offer_id")] string OfferId,
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("actor_type")] string ActorType,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);
