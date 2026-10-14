using Mono.Api.Domain;

namespace Mono.Api.Services;

public enum DeliveryRequestRouteLocationStatus
{
    CoordinatesPending,
    Registered,
    Conflict
}

public sealed record DeliveryRequestRouteLocationRegistration(
    string PickupLocationId,
    string DropoffLocationId,
    DeliveryRequestRouteLocationStatus Status);

/// <summary>
/// Derives stable route location IDs from a persisted delivery request and seeds supplied
/// coordinates into the catalog. It does not geocode address-only input.
/// </summary>
public sealed class DeliveryRequestRouteLocationRegistrar(IRouteLocationCoordinateCatalog catalog)
{
    public async Task<DeliveryRequestRouteLocationRegistration> RegisterAsync(
        string deliveryRequestId,
        DeliveryRequestInput request,
        CancellationToken cancellationToken = default)
    {
        if (!DeliveryRequestInputValidator.IsValidDeliveryRequestId(deliveryRequestId))
            throw new ArgumentException("A valid persisted delivery request ID is required.", nameof(deliveryRequestId));
        ArgumentNullException.ThrowIfNull(request);

        var suffix = deliveryRequestId[4..];
        var pickupId = $"loc_{suffix}_pickup";
        var dropoffId = $"loc_{suffix}_dropoff";
        var pickup = request.Pickup;
        var dropoff = request.Dropoff;
        if (pickup is null || dropoff is null ||
            pickup.Latitude is not { } pickupLatitude || pickup.Longitude is not { } pickupLongitude ||
            dropoff.Latitude is not { } dropoffLatitude || dropoff.Longitude is not { } dropoffLongitude)
        {
            return new DeliveryRequestRouteLocationRegistration(
                pickupId, dropoffId, DeliveryRequestRouteLocationStatus.CoordinatesPending);
        }

        var pickupResult = await catalog.WriteAsync(new RouteLocationCoordinateWriteCommand(
            $"route-location-seed:{deliveryRequestId}:pickup", pickupId, 0,
            new GeoCoordinate((double)pickupLatitude, (double)pickupLongitude)), cancellationToken);
        if (pickupResult.Outcome == RouteLocationCoordinateWriteOutcome.Conflict)
        {
            return new DeliveryRequestRouteLocationRegistration(
                pickupId, dropoffId, DeliveryRequestRouteLocationStatus.Conflict);
        }

        var dropoffResult = await catalog.WriteAsync(new RouteLocationCoordinateWriteCommand(
            $"route-location-seed:{deliveryRequestId}:dropoff", dropoffId, 0,
            new GeoCoordinate((double)dropoffLatitude, (double)dropoffLongitude)), cancellationToken);

        var status = pickupResult.Outcome == RouteLocationCoordinateWriteOutcome.Conflict ||
                     dropoffResult.Outcome == RouteLocationCoordinateWriteOutcome.Conflict
            ? DeliveryRequestRouteLocationStatus.Conflict
            : DeliveryRequestRouteLocationStatus.Registered;
        return new DeliveryRequestRouteLocationRegistration(pickupId, dropoffId, status);
    }
}
