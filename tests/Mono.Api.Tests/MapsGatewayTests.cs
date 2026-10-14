using Mono.Api.Domain;
using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class MapsGatewayTests
{
    [Fact]
    public async Task GeocodeTrimsInputAndProviderAddress()
    {
        var provider = new StubMapsProvider
        {
            GeocodeResult = new GeocodeResult(
                new GeoCoordinate(35.7, 51.4), " Tehran ")
        };
        var gateway = new MapsGateway(provider);

        var result = await gateway.GeocodeAsync("  Tehran  ");

        Assert.Equal("Tehran", provider.LastAddress);
        Assert.Equal(new GeoCoordinate(35.7, 51.4), result!.Location);
        Assert.Equal("Tehran", result.FormattedAddress);
    }

    [Fact]
    public async Task GeocodeRejectsInvalidProviderCoordinate()
    {
        var gateway = new MapsGateway(new StubMapsProvider
        {
            GeocodeResult = new GeocodeResult(new GeoCoordinate(91, 51), "Invalid")
        });

        await Assert.ThrowsAsync<MapsProviderContractException>(() =>
            gateway.GeocodeAsync("Some address"));
    }

    [Fact]
    public async Task ReverseGeocodeRejectsInvalidInputBeforeCallingProvider()
    {
        var provider = new StubMapsProvider();
        var gateway = new MapsGateway(provider);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            gateway.ReverseGeocodeAsync(new GeoCoordinate(0, 181)));

        Assert.False(provider.ReverseGeocodeCalled);
    }

    [Fact]
    public async Task RouteValidatesEveryLocationBeforeCallingProvider()
    {
        var provider = new StubMapsProvider();
        var gateway = new MapsGateway(provider);
        var query = new RouteQuery(
            new GeoCoordinate(35.7, 51.4),
            new GeoCoordinate(35.8, 51.5),
            RouteTravelMode.Driving,
            [new GeoCoordinate(double.NaN, 51.6)]);

        await Assert.ThrowsAsync<ArgumentException>(() => gateway.GetRouteAsync(query));

        Assert.False(provider.RouteCalled);
    }

    [Fact]
    public async Task RouteRejectsInvalidProviderEstimates()
    {
        var provider = new StubMapsProvider
        {
            RouteEstimate = new RouteEstimate(-1, 60)
        };
        var gateway = new MapsGateway(provider);

        await Assert.ThrowsAsync<MapsProviderContractException>(() =>
            gateway.GetRouteAsync(new RouteQuery(
                new GeoCoordinate(35.7, 51.4), new GeoCoordinate(35.8, 51.5), RouteTravelMode.Cycling)));
    }

    [Fact]
    public async Task RouteMatrixAllowsUnreachablePairsAndPreservesDimensions()
    {
        var provider = new StubMapsProvider
        {
            Matrix =
            [
                [
                    new RouteMatrixCell(1200, 240),
                    new RouteMatrixCell(null, null)
                ]
            ]
        };
        var gateway = new MapsGateway(provider);

        var result = await gateway.GetRouteMatrixAsync(new RouteMatrixQuery(
            [new GeoCoordinate(35.7, 51.4)],
            [new GeoCoordinate(35.8, 51.5), new GeoCoordinate(35.9, 51.6)],
            RouteTravelMode.Walking));

        Assert.Single(result);
        Assert.Equal(2, result[0].Count);
        Assert.Null(result[0][1].DistanceMeters);
    }

    [Fact]
    public async Task RouteMatrixRejectsProviderDimensionMismatch()
    {
        var provider = new StubMapsProvider { Matrix = [] };
        var gateway = new MapsGateway(provider);

        await Assert.ThrowsAsync<MapsProviderContractException>(() =>
            gateway.GetRouteMatrixAsync(new RouteMatrixQuery(
                [new GeoCoordinate(35.7, 51.4)],
                [new GeoCoordinate(35.8, 51.5)], RouteTravelMode.Driving)));
    }

    private sealed class StubMapsProvider : IMapsProvider
    {
        public GeocodeResult? GeocodeResult { get; init; }
        public string? ReverseAddress { get; init; }
        public RouteEstimate? RouteEstimate { get; init; }
        public IReadOnlyList<IReadOnlyList<RouteMatrixCell>> Matrix { get; init; } = [];
        public string? LastAddress { get; private set; }
        public bool ReverseGeocodeCalled { get; private set; }
        public bool RouteCalled { get; private set; }

        public Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken cancellationToken = default)
        {
            LastAddress = address;
            return Task.FromResult(GeocodeResult);
        }

        public Task<string?> ReverseGeocodeAsync(
            GeoCoordinate location, CancellationToken cancellationToken = default)
        {
            ReverseGeocodeCalled = true;
            return Task.FromResult(ReverseAddress);
        }

        public Task<RouteEstimate?> GetRouteAsync(
            RouteQuery query, CancellationToken cancellationToken = default)
        {
            RouteCalled = true;
            return Task.FromResult(RouteEstimate);
        }

        public Task<IReadOnlyList<IReadOnlyList<RouteMatrixCell>>> GetRouteMatrixAsync(
            RouteMatrixQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(Matrix);
    }
}
