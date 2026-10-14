using Mono.Api.Domain;
using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class DeliveryRequestRouteLocationRegistrarTests
{
    private const string RequestId = "drq_0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Registers_stable_location_ids_and_coordinates_from_a_delivery_request()
    {
        var catalog = new FakeCoordinateCatalog();
        var registrar = new DeliveryRequestRouteLocationRegistrar(catalog);

        var first = await registrar.RegisterAsync(RequestId, RequestWithCoordinates());
        var replay = await registrar.RegisterAsync(RequestId, RequestWithCoordinates());

        Assert.Equal("loc_0123456789abcdef0123456789abcdef_pickup", first.PickupLocationId);
        Assert.Equal("loc_0123456789abcdef0123456789abcdef_dropoff", first.DropoffLocationId);
        Assert.Equal(DeliveryRequestRouteLocationStatus.Registered, first.Status);
        Assert.Equal(first.PickupLocationId, replay.PickupLocationId);
        Assert.Equal(4, catalog.Commands.Count);
        Assert.Equal(catalog.Commands[0].CommandId, catalog.Commands[2].CommandId);
        Assert.Equal(catalog.Commands[1].CommandId, catalog.Commands[3].CommandId);
        Assert.Equal(0, catalog.Commands[0].ExpectedVersion);
        Assert.Equal("route-location-seed:drq_0123456789abcdef0123456789abcdef:pickup", catalog.Commands[0].CommandId);
        Assert.Equal(new GeoCoordinate(35.7, 51.4), catalog.Coordinates[first.PickupLocationId]);
        Assert.Equal(new GeoCoordinate(35.8, 51.5), catalog.Coordinates[first.DropoffLocationId]);
    }

    [Fact]
    public async Task Leaves_address_only_locations_pending_without_geocoding()
    {
        var catalog = new FakeCoordinateCatalog();
        var registrar = new DeliveryRequestRouteLocationRegistrar(catalog);

        var result = await registrar.RegisterAsync(RequestId, new DeliveryRequestInput
        {
            Pickup = new DeliveryLocationInput { AddressText = "Pickup address" },
            Dropoff = new DeliveryLocationInput { AddressText = "Dropoff address" }
        });

        Assert.Equal(DeliveryRequestRouteLocationStatus.CoordinatesPending, result.Status);
        Assert.Empty(catalog.Commands);
    }

    [Fact]
    public async Task Stops_registration_when_a_location_id_conflicts_with_existing_data()
    {
        var catalog = new FakeCoordinateCatalog(RouteLocationCoordinateWriteOutcome.Conflict);
        var registrar = new DeliveryRequestRouteLocationRegistrar(catalog);

        var result = await registrar.RegisterAsync(RequestId, RequestWithCoordinates());

        Assert.Equal(DeliveryRequestRouteLocationStatus.Conflict, result.Status);
        Assert.Single(catalog.Commands);
    }

    private static DeliveryRequestInput RequestWithCoordinates() => new()
    {
        Pickup = new DeliveryLocationInput { Latitude = 35.7m, Longitude = 51.4m },
        Dropoff = new DeliveryLocationInput { Latitude = 35.8m, Longitude = 51.5m }
    };

    private sealed class FakeCoordinateCatalog(
        RouteLocationCoordinateWriteOutcome outcome = RouteLocationCoordinateWriteOutcome.Persisted)
        : IRouteLocationCoordinateCatalog
    {
        public List<RouteLocationCoordinateWriteCommand> Commands { get; } = [];
        public Dictionary<string, GeoCoordinate> Coordinates { get; } = new(StringComparer.Ordinal);

        public Task<RouteLocationCoordinateWriteResult> WriteAsync(
            RouteLocationCoordinateWriteCommand command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            if (outcome is RouteLocationCoordinateWriteOutcome.Persisted or RouteLocationCoordinateWriteOutcome.Replayed)
                Coordinates[command.LocationId] = command.Coordinate;
            return Task.FromResult(new RouteLocationCoordinateWriteResult(outcome, command.LocationId, 1));
        }

        public Task<IReadOnlyDictionary<string, GeoCoordinate>> ResolveAsync(
            IReadOnlyList<string> locationIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, GeoCoordinate>>(
                locationIds.Where(Coordinates.ContainsKey).ToDictionary(id => id, id => Coordinates[id], StringComparer.Ordinal));

        public Task<RouteLocationCoordinateVersion?> FindVersionAsync(
            string locationId, int version, CancellationToken cancellationToken = default) =>
            Task.FromResult<RouteLocationCoordinateVersion?>(null);
    }
}
