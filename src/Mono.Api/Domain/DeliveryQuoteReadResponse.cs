using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record DeliveryQuoteReadResponse(
    [property: JsonPropertyName("quote_id")] string QuoteId,
    [property: JsonPropertyName("delivery_request_id")] string DeliveryRequestId,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("issued_at")] DateTimeOffset IssuedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("is_expired")] bool IsExpired,
    [property: JsonPropertyName("distance_meters")] decimal DistanceMeters,
    [property: JsonPropertyName("fare_toman")] int FareToman,
    [property: JsonPropertyName("buyer_share_toman")] int BuyerShareToman,
    [property: JsonPropertyName("seller_share_toman")] int SellerShareToman,
    [property: JsonPropertyName("recorded_at")] DateTimeOffset RecordedAt);

public sealed record DeliveryQuoteVersionPageResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<DeliveryQuoteReadResponse> Items,
    [property: JsonPropertyName("next_before_version")] int? NextBeforeVersion);
