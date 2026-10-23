using System.Text.Json.Serialization;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierLocationInput(
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("accuracy_meters")] double? AccuracyMeters,
    [property: JsonPropertyName("heading_degrees")] double? HeadingDegrees,
    [property: JsonPropertyName("speed_meters_per_second")] double? SpeedMetersPerSecond,
    [property: JsonPropertyName("observed_at")] DateTimeOffset? ObservedAt,
    [property: JsonPropertyName("active_route_id")] string? ActiveRouteId);

public static class MonoCourierLocationEndpoints
{
    public static IEndpointRouteBuilder MapMonoCourierLocationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/courier/v1/location", async (
                CourierLocationInput input,
                IMonoCourierContext courier,
                PostgresCourierCurrentLocationService locations,
                CancellationToken cancellationToken) =>
            {
                var update = new CourierLocationUpdate
                {
                    CourierId = courier.CourierId,
                    Latitude = input.Latitude,
                    Longitude = input.Longitude,
                    AccuracyMeters = input.AccuracyMeters,
                    HeadingDegrees = input.HeadingDegrees,
                    SpeedMetersPerSecond = input.SpeedMetersPerSecond,
                    ObservedAt = input.ObservedAt,
                    ActiveRouteId = input.ActiveRouteId
                };
                try
                {
                    var result = await locations.UpdateAsync(courier.CourierId, update, cancellationToken);
                    return Results.Ok(new
                    {
                        courier_id = result.Position.CourierId,
                        observed_at = result.Position.ObservedAt,
                        applied = result.Applied
                    });
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new { error = new { code = "INVALID_REQUEST", message = "Courier location fields are invalid." } });
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new { error = new { code = "COURIER_NOT_FOUND", message = "The courier was not found." } });
                }
            })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        return endpoints;
    }
}
