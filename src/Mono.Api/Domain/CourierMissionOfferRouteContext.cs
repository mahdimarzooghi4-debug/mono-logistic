using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

/// <summary>Privacy-limited map context for one unexpired offer owned by the authenticated courier.</summary>
public sealed record CourierMissionOfferRouteContext(
    [property: JsonPropertyName("offer_id")] string OfferId,
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("coordinates_status")] string CoordinatesStatus,
    [property: JsonPropertyName("pickup")] CourierMissionOfferRoutePoint Pickup,
    [property: JsonPropertyName("dropoff")] CourierMissionOfferRoutePoint Dropoff)
{
    public static CourierMissionOfferRouteContext Create(
        string offerId,
        string missionId,
        string deliveryRequestId,
        DateTimeOffset expiresAt,
        IReadOnlyDictionary<string, GeoCoordinate> resolvedCoordinates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(offerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        if (!DeliveryRequestInputValidator.IsValidDeliveryRequestId(deliveryRequestId))
            throw new ArgumentException("A valid delivery request ID is required.", nameof(deliveryRequestId));
        ArgumentNullException.ThrowIfNull(resolvedCoordinates);
        if (expiresAt == default || expiresAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Offer expiry must be a UTC timestamp.", nameof(expiresAt));

        var suffix = deliveryRequestId[4..];
        var pickupId = $"loc_{suffix}_pickup";
        var dropoffId = $"loc_{suffix}_dropoff";
        var pickup = ResolvePoint(pickupId, resolvedCoordinates);
        var dropoff = ResolvePoint(dropoffId, resolvedCoordinates);
        var complete = pickup.Coordinate is not null && dropoff.Coordinate is not null;

        return new CourierMissionOfferRouteContext(
            offerId,
            missionId,
            expiresAt,
            complete ? "COMPLETE" : "COORDINATES_PENDING",
            pickup,
            dropoff);
    }

    private static CourierMissionOfferRoutePoint ResolvePoint(
        string locationId,
        IReadOnlyDictionary<string, GeoCoordinate> coordinates)
    {
        if (!coordinates.TryGetValue(locationId, out var coordinate) || coordinate is null || !coordinate.IsValid)
            return new CourierMissionOfferRoutePoint(locationId, null);
        return new CourierMissionOfferRoutePoint(locationId, coordinate);
    }
}

public sealed record CourierMissionOfferRoutePoint(
    [property: JsonPropertyName("location_id")] string LocationId,
    [property: JsonPropertyName("coordinate")] GeoCoordinate? Coordinate);
