using Microsoft.AspNetCore.Mvc;
using Mono.Api.Security;
using Mono.Api.Services;

namespace Mono.Api.Api;

public static class MonoZoneGeometryGeoJsonEndpoints
{
    public static IEndpointRouteBuilder MapMonoZoneGeometryGeoJsonEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/operations/v1/zones/{zoneId}/geometry/{geometryVersion}/geojson",
            async (
                string zoneId,
                string geometryVersion,
                [FromServices] MonoZoneGeometryGeoJsonService geoJson,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await geoJson.GetAsync(
                        zoneId, geometryVersion, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(new ApiError(
                        "INVALID_ZONE_GEOMETRY_REFERENCE",
                        "Zone ID or geometry version is invalid.")));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new ApiErrorResponse(new ApiError(
                        "ZONE_GEOMETRY_VERSION_NOT_FOUND", "Zone geometry version was not found.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}
