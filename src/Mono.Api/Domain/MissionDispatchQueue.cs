using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record MissionDispatchQueueOffer(
    [property: JsonPropertyName("offer_id")] string OfferId,
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("zone_id")] string ZoneId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record MissionDispatchQueueItem(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("customer_id")] string CustomerId,
    [property: JsonPropertyName("delivery_request_id")] string DeliveryRequestId,
    [property: JsonPropertyName("external_reference")] string ExternalReference,
    [property: JsonPropertyName("committed_delivery_at")] DateTimeOffset CommittedDeliveryAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("active_offer")] MissionDispatchQueueOffer? ActiveOffer);

public sealed record MissionDispatchQueuePage(
    [property: JsonPropertyName("items")] IReadOnlyList<MissionDispatchQueueItem> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);
