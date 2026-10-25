using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

/// <summary>Privacy-limited route context for the courier's currently assigned active mission.</summary>
public sealed record CourierActiveMissionRouteContext(
    [property: JsonPropertyName("offer_id")] string OfferId,
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("coordinates_status")] string CoordinatesStatus,
    [property: JsonPropertyName("pickup")] CourierMissionOfferRoutePoint Pickup,
    [property: JsonPropertyName("dropoff")] CourierMissionOfferRoutePoint Dropoff)
{
    private static readonly HashSet<string> ActiveStatuses = new(StringComparer.Ordinal)
    {
        "COURIER_ASSIGNED", "ARRIVED_PICKUP", "HANDED_TO_COURIER", "PICKED_UP", "IN_TRANSIT", "ARRIVED_DROPOFF"
    };

    public static CourierActiveMissionRouteContext Create(string offerId, string missionId, string status,
        string deliveryRequestId, IReadOnlyDictionary<string, GeoCoordinate> resolvedCoordinates)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(offerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(missionId);
        if (!ActiveStatuses.Contains(status))
            throw new ArgumentException("Mission status is not active for courier execution.", nameof(status));
        if (!DeliveryRequestInputValidator.IsValidDeliveryRequestId(deliveryRequestId))
            throw new ArgumentException("A valid delivery request ID is required.", nameof(deliveryRequestId));
        ArgumentNullException.ThrowIfNull(resolvedCoordinates);
        var suffix = deliveryRequestId[4..];
        var pickup = ResolvePoint($"loc_{suffix}_pickup", resolvedCoordinates);
        var dropoff = ResolvePoint($"loc_{suffix}_dropoff", resolvedCoordinates);
        return new CourierActiveMissionRouteContext(offerId, missionId, status,
            pickup.Coordinate is not null && dropoff.Coordinate is not null ? "COMPLETE" : "COORDINATES_PENDING",
            pickup, dropoff);
    }

    private static CourierMissionOfferRoutePoint ResolvePoint(string id, IReadOnlyDictionary<string, GeoCoordinate> coordinates) =>
        coordinates.TryGetValue(id, out var coordinate) && coordinate is not null && coordinate.IsValid
            ? new CourierMissionOfferRoutePoint(id, coordinate)
            : new CourierMissionOfferRoutePoint(id, null);
}
