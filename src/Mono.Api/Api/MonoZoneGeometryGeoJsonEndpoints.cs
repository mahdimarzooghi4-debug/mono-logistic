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
                HttpContext httpContext,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var feature = await geoJson.GetAsync(
                        zoneId, geometryVersion, cancellationToken);
                    var etag = MonoGeoJsonCacheValidators.ForSnapshot(feature.Properties.SnapshotHash);
                    httpContext.Response.Headers["ETag"] = etag;
                    httpContext.Response.Headers["Cache-Control"] = "private, max-age=31536000, immutable";
                    if (MatchesIfNoneMatch(httpContext.Request.Headers["If-None-Match"], etag))
                        return Results.StatusCode(StatusCodes.Status304NotModified);
                    return Results.Ok(feature);
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

    private static bool MatchesIfNoneMatch(string? header, string etag) =>
        header?.Split(',').Select(value => value.Trim())
            .Any(value => value == "*" || string.Equals(value, etag, StringComparison.Ordinal)) == true;
}
