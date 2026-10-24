using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record MissionCourierOfferBatchCommand(
    string MissionId,
    string CourierId,
    string ZoneId,
    string IdempotencyKey);

public sealed record MissionCourierOfferBatchItem(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("zone_id")] string ZoneId,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("offer")] MissionCourierOffer? Offer);
