using Mono.Api.Domain;
using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class DeliveryLocationResolverTests
{
    [Fact]
    public async Task Valid_coordinates_are_returned_without_geocoding()
    {
        var maps = new StubMapsGateway();
        var resolver = new DeliveryLocationResolver(maps);

        var result = await resolver.ResolveAsync(new DeliveryLocationInput
        {
            AddressText = " Store front ",
            Latitude = 35.7m,
            Longitude = 51.4m
        });

        Assert.NotNull(result);
        Assert.Equal(new GeoCoordinate(35.7, 51.4), result.Location);
        Assert.Equal("Store front", result.AddressText);
        Assert.Equal(LocationResolutionSource.ProvidedCoordinates, result.Source);
        Assert.Equal(0, maps.GeocodeCalls);
    }

    [Fact]
    public async Task Address_only_location_uses_maps_gateway_and_normalizes_result()
    {
        var maps = new StubMapsGateway
        {
            GeocodeResult = new GeocodeResult(new GeoCoordinate(35.7, 51.4), " Tehran ")
        };
        var resolver = new DeliveryLocationResolver(maps);

        var result = await resolver.ResolveAsync(new DeliveryLocationInput
        {
            AddressText = "  Tehran  "
        });

        Assert.NotNull(result);
        Assert.Equal("Tehran", maps.LastAddress);
        Assert.Equal(new GeoCoordinate(35.7, 51.4), result.Location);
        Assert.Equal("Tehran", result.AddressText);
        Assert.Equal(LocationResolutionSource.GeocodedAddress, result.Source);
    }

    [Fact]
    public async Task Address_only_location_uses_input_when_provider_omits_formatted_address()
    {
        var maps = new StubMapsGateway
        {
            GeocodeResult = new GeocodeResult(new GeoCoordinate(35.7, 51.4), null)
        };
        var resolver = new DeliveryLocationResolver(maps);

        var result = await resolver.ResolveAsync(new DeliveryLocationInput { AddressText = "Tehran" });

        Assert.Equal("Tehran", result!.AddressText);
    }

    [Fact]
    public async Task No_geocoding_match_remains_unresolved()
    {
        var resolver = new DeliveryLocationResolver(new StubMapsGateway());

        var result = await resolver.ResolveAsync(new DeliveryLocationInput { AddressText = "Unknown place" });

        Assert.Null(result);
    }

    [Fact]
    public async Task Partial_coordinates_are_rejected_without_geocoding()
    {
        var maps = new StubMapsGateway();
        var resolver = new DeliveryLocationResolver(maps);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            resolver.ResolveAsync(new DeliveryLocationInput
            {
                AddressText = "Tehran",
                Latitude = 35.7m
            }));

        Assert.Equal(0, maps.GeocodeCalls);
    }

    [Theory]
    [InlineData(90.1, 51.4)]
    [InlineData(35.7, -180.1)]
    public async Task Out_of_range_coordinates_are_rejected_without_geocoding(
        double latitude,
        double longitude)
    {
        var maps = new StubMapsGateway();
        var resolver = new DeliveryLocationResolver(maps);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            resolver.ResolveAsync(new DeliveryLocationInput
            {
                AddressText = "Tehran",
                Latitude = (decimal)latitude,
                Longitude = (decimal)longitude
            }));

        Assert.Equal(0, maps.GeocodeCalls);
    }

    [Fact]
    public async Task Missing_address_and_coordinates_are_rejected()
    {
        var resolver = new DeliveryLocationResolver(new StubMapsGateway());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            resolver.ResolveAsync(new DeliveryLocationInput()));
    }

    [Fact]
    public async Task Geocoding_receives_the_callers_cancellation_token()
    {
        using var cancellation = new CancellationTokenSource();
        var maps = new StubMapsGateway();
        var resolver = new DeliveryLocationResolver(maps);

        await resolver.ResolveAsync(
            new DeliveryLocationInput { AddressText = "Tehran" },
            cancellation.Token);

        Assert.Equal(cancellation.Token, maps.LastCancellationToken);
    }

    private sealed class StubMapsGateway : IMapsGateway
    {
        public GeocodeResult? GeocodeResult { get; init; }
        public int GeocodeCalls { get; private set; }
        public string? LastAddress { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        public Task<GeocodeResult?> GeocodeAsync(
            string address,
            CancellationToken cancellationToken = default)
        {
            GeocodeCalls++;
            LastAddress = address;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(GeocodeResult);
        }

        public Task<string?> ReverseGeocodeAsync(
            GeoCoordinate location,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RouteEstimate?> GetRouteAsync(
            RouteQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<IReadOnlyList<RouteMatrixCell>>> GetRouteMatrixAsync(
            RouteMatrixQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
