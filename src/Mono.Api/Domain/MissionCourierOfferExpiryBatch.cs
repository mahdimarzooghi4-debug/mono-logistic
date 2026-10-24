using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record MissionCourierOfferExpiryCommand(
    string MissionId,
    string OfferId,
    string IdempotencyKey);

public sealed record MissionCourierOfferExpiryBatchItem(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("offer_id")] string OfferId,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("offer")] MissionCourierOffer? Offer,
    [property: JsonPropertyName("mission_status")] string? MissionStatus);
