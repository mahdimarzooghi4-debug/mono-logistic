using Mono.Api.Domain;
using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class RouteMatrixAcquisitionServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Resolves_all_locations_and_builds_unsigned_directed_matrix_for_requested_mode()
    {
        var resolver = new StubCoordinateResolver();
        var maps = new StubMapsGateway();
        var service = new RouteMatrixAcquisitionService(maps, resolver);

        var result = await service.AcquireAsync(Request(RouteTravelMode.Bike));

        Assert.Equal(new[] { "A", "B", "D2", "P2", "start" }, resolver.RequestedIds);
        Assert.Equal(RouteTravelMode.Bike, maps.LastMatrixQuery!.Mode);
        Assert.Equal(5, maps.LastMatrixQuery.Origins.Count);
        Assert.False(result.Matrix.SourceVerified);
        Assert.Empty(result.Attestation);
        Assert.Equal(RouteTravelMode.Bike, result.TravelMode);
        Assert.Equal(31, result.Matrix.TravelTimeSeconds["A"]["B"]);
        Assert.Equal(34, result.Matrix.TravelTimeSeconds["B"]["A"]);
        Assert.Equal(5, result.LocationCoordinates!.Count);
    }

    [Fact]
    public async Task Fails_closed_when_any_route_location_has_no_coordinate()
    {
        var service = new RouteMatrixAcquisitionService(
            new StubMapsGateway(), new StubCoordinateResolver(omitDropoff: true));

        await Assert.ThrowsAsync<RouteLocationResolutionException>(() =>
            service.AcquireAsync(Request(RouteTravelMode.Walk)));
    }

    private static RouteMatrixAcquisitionRequest Request(RouteTravelMode mode) => new(
        "route-1", 3,
        new RoutePlanSnapshot(
            "route-1", "start", Now.AddMinutes(-3),
            [new RouteMissionDeadline("mission-1", Now.AddHours(1))],
            [
                new RouteInsertionStop("mission-1", RouteStopKind.Pickup, "A", 20, Now.AddMinutes(-3)),
                new RouteInsertionStop("mission-1", RouteStopKind.Dropoff, "B", 30)
            ]),
        new RouteInsertionMission("mission-2", "P2", "D2", Now, Now.AddHours(2), 15, 30),
        mode, "maps-source", "evidence-1", Now);

    private sealed class StubCoordinateResolver(bool omitDropoff = false) : IRouteLocationCoordinateResolver
    {
        public IReadOnlyList<string> RequestedIds { get; private set; } = [];

        public Task<IReadOnlyDictionary<string, GeoCoordinate>> ResolveAsync(
            IReadOnlyList<string> locationIds, CancellationToken cancellationToken = default)
        {
            RequestedIds = locationIds;
            IReadOnlyDictionary<string, GeoCoordinate> result = locationIds
                .Where(id => !omitDropoff || id != "D2")
                .ToDictionary(id => id, id => new GeoCoordinate(35.7, 51.4), StringComparer.Ordinal);
            return Task.FromResult(result);
        }
    }

    private sealed class StubMapsGateway : IMapsGateway
    {
        public RouteMatrixQuery? LastMatrixQuery { get; private set; }

        public Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<string?> ReverseGeocodeAsync(GeoCoordinate location, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<RouteEstimate?> GetRouteAsync(RouteQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IReadOnlyList<RouteMatrixCell>>> GetRouteMatrixAsync(
            RouteMatrixQuery query, CancellationToken cancellationToken = default)
        {
            LastMatrixQuery = query;
            var cells = query.Origins.Select((_, row) =>
                (IReadOnlyList<RouteMatrixCell>)query.Destinations.Select((_, column) =>
                    new RouteMatrixCell(100, 30 + row * 4 + column)).ToArray()).ToArray();
            return Task.FromResult<IReadOnlyList<IReadOnlyList<RouteMatrixCell>>>(cells);
        }
    }
}
