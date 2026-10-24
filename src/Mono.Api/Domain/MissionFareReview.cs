using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record MissionFareReviewItem(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("customer_id")] string CustomerId,
    [property: JsonPropertyName("delivery_request_id")] string DeliveryRequestId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("committed_delivery_at")] DateTimeOffset CommittedDeliveryAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("quote_id")] string QuoteId,
    [property: JsonPropertyName("quote_version")] int QuoteVersion,
    [property: JsonPropertyName("distance_meters")] decimal DistanceMeters,
    [property: JsonPropertyName("fare_toman")] int FareToman,
    [property: JsonPropertyName("buyer_share_toman")] int BuyerShareToman,
    [property: JsonPropertyName("seller_share_toman")] int SellerShareToman,
    [property: JsonPropertyName("mono_commission_estimate_toman")] decimal MonoCommissionEstimateToman,
    [property: JsonPropertyName("commission_vat_estimate_toman")] decimal CommissionVatEstimateToman,
    [property: JsonPropertyName("courier_net_estimate_toman")] decimal CourierNetEstimateToman)
{
    public static MissionFareReviewItem Create(
        string missionId,
        string customerId,
        string deliveryRequestId,
        string status,
        DateTimeOffset committedDeliveryAt,
        DateTimeOffset createdAt,
        string quoteId,
        int quoteVersion,
        decimal distanceMeters,
        int fareToman,
        int buyerShareToman,
        int sellerShareToman)
    {
        if (fareToman <= 0 || buyerShareToman < 0 || sellerShareToman < 0 ||
            buyerShareToman + sellerShareToman != fareToman ||
            quoteVersion < 1 || distanceMeters < 0)
            throw new ArgumentException("The recorded fare snapshot is invalid.");

        var commission = fareToman * 0.10m;
        var vat = commission * 0.10m;
        return new MissionFareReviewItem(
            missionId, customerId, deliveryRequestId, status, committedDeliveryAt, createdAt,
            quoteId, quoteVersion, distanceMeters, fareToman, buyerShareToman, sellerShareToman,
            commission, vat, fareToman - commission - vat);
    }
}

public sealed record MissionFareReviewPage(
    [property: JsonPropertyName("items")] IReadOnlyList<MissionFareReviewItem> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);
