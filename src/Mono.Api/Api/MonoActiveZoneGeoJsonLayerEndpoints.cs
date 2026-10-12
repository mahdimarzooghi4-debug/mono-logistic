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
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var page = await layers.GetAsync(kind, pageSize, cursor, cancellationToken);
                var etag = MonoGeoJsonCacheValidators.ForActiveLayerPage(
                    page.Kind, page.GeoJson.Features, page.HasMore, page.NextCursor);
                httpContext.Response.Headers["ETag"] = etag;
                httpContext.Response.Headers["Cache-Control"] = "private, no-cache";
                if (MatchesIfNoneMatch(httpContext.Request.Headers["If-None-Match"], etag))
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                return Results.Ok(page);
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

    private static bool MatchesIfNoneMatch(string? header, string etag) =>
        header?.Split(',').Select(value => value.Trim())
            .Any(value => value == "*" || string.Equals(value, etag, StringComparison.Ordinal)) == true;
}
