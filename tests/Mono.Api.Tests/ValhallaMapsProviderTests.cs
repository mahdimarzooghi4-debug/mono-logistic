using System.Net;
using System.Text;
using System.Text.Json;
using Mono.Api.Domain;
using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class ValhallaMapsProviderTests
{
    [Fact]
    public async Task RouteUsesPedestrianCostingAndConvertsKilometersAndFractionalSeconds()
    {
        var handler = new StubHttpMessageHandler(
            """{"trip":{"status":0,"summary":{"length":1.25,"time":80.2}}}""");
        var provider = CreateProvider(handler);

        var result = await provider.GetRouteAsync(new RouteQuery(
            new GeoCoordinate(35.7, 51.4),
            new GeoCoordinate(35.8, 51.5),
            RouteTravelMode.Walk,
            [new GeoCoordinate(35.75, 51.45)]));

        Assert.Equal(1250d, result!.DistanceMeters);
        Assert.Equal(81, result.EstimatedDurationSeconds);
        using var request = ParseRequest(handler.RequestUri!);
        Assert.Equal("pedestrian", request.RootElement.GetProperty("costing").GetString());
        Assert.Equal("kilometers", request.RootElement.GetProperty("units").GetString());
        var locations = request.RootElement.GetProperty("locations");
        Assert.Equal(3, locations.GetArrayLength());
        Assert.Equal(35.7, locations[0].GetProperty("lat").GetDouble());
        Assert.Equal(51.5, locations[2].GetProperty("lon").GetDouble());
    }

    [Theory]
    [InlineData(RouteTravelMode.Car, "auto")]
    [InlineData(RouteTravelMode.Bike, "bicycle")]
    [InlineData(RouteTravelMode.Walk, "pedestrian")]
    public async Task MatrixUsesCostingForRequestedModeAndPreservesUnreachablePairs(
        RouteTravelMode mode, string expectedCosting)
    {
        var handler = new StubHttpMessageHandler(
            """{"sources_to_targets":{"durations":[[0,null],[42.2,9]],"distances":[[0,null],[0.87,0.04]]}}""");
        var provider = CreateProvider(handler);

        var result = await provider.GetRouteMatrixAsync(new RouteMatrixQuery(
            [new GeoCoordinate(35.7, 51.4), new GeoCoordinate(35.8, 51.5)],
            [new GeoCoordinate(35.9, 51.6), new GeoCoordinate(35.95, 51.65)],
            mode));

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result[0].Count);
        Assert.Equal(new RouteMatrixCell(null, null), result[0][1]);
        Assert.Equal(new RouteMatrixCell(870, 43), result[1][0]);
        using var request = ParseRequest(handler.RequestUri!);
        Assert.Equal(expectedCosting, request.RootElement.GetProperty("costing").GetString());
        Assert.False(request.RootElement.GetProperty("verbose").GetBoolean());
    }

    [Fact]
    public async Task MatrixRejectsMismatchedDistanceAndDurationDimensions()
    {
        var handler = new StubHttpMessageHandler(
            """{"sources_to_targets":{"durations":[[1]],"distances":[]}}""");
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<MapsProviderContractException>(() =>
            provider.GetRouteMatrixAsync(new RouteMatrixQuery(
                [new GeoCoordinate(35.7, 51.4)],
                [new GeoCoordinate(35.8, 51.5)],
                RouteTravelMode.Car)));
    }

    [Fact]
    public async Task ProviderSurfacesNonSuccessFromPrivateRoutingService()
    {
        var handler = new StubHttpMessageHandler("unavailable", HttpStatusCode.ServiceUnavailable);
        var provider = CreateProvider(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            provider.GetRouteAsync(new RouteQuery(
                new GeoCoordinate(35.7, 51.4),
                new GeoCoordinate(35.8, 51.5),
                RouteTravelMode.Car)));
    }

    private static ValhallaMapsProvider CreateProvider(StubHttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://valhalla.test:8002/") };
        return new ValhallaMapsProvider(client);
    }

    private static JsonDocument ParseRequest(Uri uri)
    {
        const string prefix = "?json=";
        Assert.True(uri.Query.StartsWith(prefix, StringComparison.Ordinal));
        return JsonDocument.Parse(Uri.UnescapeDataString(uri.Query[prefix.Length..]));
    }

    private sealed class StubHttpMessageHandler(
        string responseBody,
        HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });
        }
    }
}
