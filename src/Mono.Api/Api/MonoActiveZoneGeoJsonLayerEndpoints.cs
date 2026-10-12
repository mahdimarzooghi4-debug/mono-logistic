using Microsoft.AspNetCore.Mvc;
using Mono.Api.Security;
using Mono.Api.Services;

namespace Mono.Api.Api;

public static class MonoActiveZoneGeoJsonLayerEndpoints
{
    public static IEndpointRouteBuilder MapMonoActiveZoneGeoJsonLayerEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/zones/active-geometries/{kind}", async (
            string kind,
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            [FromServices] MonoActiveZoneGeoJsonLayerService layers,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await layers.GetAsync(
                    kind, pageSize, cursor, cancellationToken));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_ACTIVE_GEOMETRY_QUERY",
                    "Kind, pageSize, or cursor is invalid.")));
            }
            catch (ZoneGeometryGeoJsonPageTooLargeException)
            {
                return Results.Json(new ApiErrorResponse(new ApiError(
                    "ACTIVE_GEOMETRY_PAGE_TOO_LARGE",
                    "Reduce pageSize to keep the GeoJSON response within the coordinate limit.")),
                    statusCode: StatusCodes.Status413PayloadTooLarge);
            }
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}
