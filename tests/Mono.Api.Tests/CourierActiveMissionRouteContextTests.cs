using System.Text.Json;
using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class CourierActiveMissionRouteContextTests
{
    private const string RequestId = "drq_0123456789abcdef0123456789abcdef";

    [Fact]
    public void Creates_privacy_limited_active_mission_context_with_resolved_points()
    {
        var context = CourierActiveMissionRouteContext.Create(
            "mco_0123456789abcdef0123456789abcdef", "mis_0123456789abcdef0123456789abcdef", "IN_TRANSIT", RequestId,
            new Dictionary<string, GeoCoordinate>
            {
                [$"loc_{RequestId[4..]}_pickup"] = new(35.7219, 51.3347),
                [$"loc_{RequestId[4..]}_dropoff"] = new(35.7321, 51.4123)
            });

        Assert.Equal("COMPLETE", context.CoordinatesStatus);
        Assert.Equal("IN_TRANSIT", context.Status);
        Assert.NotNull(context.Pickup.Coordinate);
        Assert.NotNull(context.Dropoff.Coordinate);
        var json = JsonSerializer.Serialize(context, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("address", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recipient", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Keeps_unresolved_coordinates_explicit_and_rejects_non_active_status()
    {
        var context = CourierActiveMissionRouteContext.Create(
            "mco_0123456789abcdef0123456789abcdef", "mis_0123456789abcdef0123456789abcdef", "COURIER_ASSIGNED", RequestId,
            new Dictionary<string, GeoCoordinate>());
        Assert.Equal("COORDINATES_PENDING", context.CoordinatesStatus);
        Assert.Null(context.Pickup.Coordinate);
        Assert.Null(context.Dropoff.Coordinate);
        Assert.Throws<ArgumentException>(() => CourierActiveMissionRouteContext.Create(
            "mco_0123456789abcdef0123456789abcdef", "mis_0123456789abcdef0123456789abcdef", "DELIVERED", RequestId,
            new Dictionary<string, GeoCoordinate>()));
    }
}
