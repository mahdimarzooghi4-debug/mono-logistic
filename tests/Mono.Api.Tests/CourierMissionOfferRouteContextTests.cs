using System.Text.Json;
using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class CourierMissionOfferRouteContextTests
{
    [Fact]
    public void Returns_current_route_points_and_only_safe_route_fields()
    {
        const string requestId = "drq_0123456789abcdef0123456789abcdef";
        var expiresAt = DateTimeOffset.Parse("2026-10-23T12:05:00Z");
        var context = CourierMissionOfferRouteContext.Create(
            "mco_0123456789abcdef0123456789abcdef",
            "mis_0123456789abcdef0123456789abcdef",
            requestId,
            expiresAt,
            new Dictionary<string, GeoCoordinate>(StringComparer.Ordinal)
            {
                ["loc_0123456789abcdef0123456789abcdef_pickup"] = new(35.7219, 51.3347),
                ["loc_0123456789abcdef0123456789abcdef_dropoff"] = new(35.7321, 51.4123)
            });

        Assert.Equal("COMPLETE", context.CoordinatesStatus);
        Assert.Equal(new GeoCoordinate(35.7219, 51.3347), context.Pickup.Coordinate);
        Assert.Equal(new GeoCoordinate(35.7321, 51.4123), context.Dropoff.Coordinate);
        Assert.Equal(expiresAt, context.ExpiresAt);

        var json = JsonSerializer.Serialize(context, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("pickup", json, StringComparison.Ordinal);
        Assert.Contains("dropoff", json, StringComparison.Ordinal);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("address", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("contact", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Keeps_missing_coordinates_explicit_instead_of_inventing_a_map_point()
    {
        var context = CourierMissionOfferRouteContext.Create(
            "mco_0123456789abcdef0123456789abcdef",
            "mis_0123456789abcdef0123456789abcdef",
            "drq_0123456789abcdef0123456789abcdef",
            DateTimeOffset.Parse("2026-10-23T12:05:00Z"),
            new Dictionary<string, GeoCoordinate>(StringComparer.Ordinal)
            {
                ["loc_0123456789abcdef0123456789abcdef_pickup"] = new(35.7219, 51.3347)
            });

        Assert.Equal("COORDINATES_PENDING", context.CoordinatesStatus);
        Assert.NotNull(context.Pickup.Coordinate);
        Assert.Null(context.Dropoff.Coordinate);
        Assert.Equal("loc_0123456789abcdef0123456789abcdef_dropoff", context.Dropoff.LocationId);
    }

    [Fact]
    public void Rejects_non_utc_expiry_and_invalid_delivery_request_id()
    {
        var coordinates = new Dictionary<string, GeoCoordinate>();
        Assert.Throws<ArgumentException>(() => CourierMissionOfferRouteContext.Create(
            "mco_0123456789abcdef0123456789abcdef",
            "mis_0123456789abcdef0123456789abcdef",
            "request",
            DateTimeOffset.Parse("2026-10-23T12:05:00Z"),
            coordinates));
        Assert.Throws<ArgumentException>(() => CourierMissionOfferRouteContext.Create(
            "mco_0123456789abcdef0123456789abcdef",
            "mis_0123456789abcdef0123456789abcdef",
            "drq_0123456789abcdef0123456789abcdef",
            new DateTimeOffset(2026, 10, 23, 12, 5, 0, TimeSpan.FromHours(3)),
            coordinates));
    }
}
