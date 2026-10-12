using Microsoft.AspNetCore.Mvc;
using Mono.Api.Security;
using Mono.Api.Services;

namespace Mono.Api.Api;

public static class MonoZoneGeometryHistoryEndpoints
{
    public static IEndpointRouteBuilder MapMonoZoneGeometryHistoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(
                "/operations/v1/zones/{zoneId}/geometry/{geometryVersion}/history")
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        group.MapGet("", async (
            string zoneId,
            string geometryVersion,
            [FromQuery] int? pageSize,
            [FromQuery] string? cursor,
            [FromServices] MonoZoneGeometryHistoryService history,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await history.GetAsync(
                    zoneId, geometryVersion, pageSize, cursor, cancellationToken));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_HISTORY_QUERY", "Zone, version, pageSize, or cursor is invalid.")));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new ApiErrorResponse(new ApiError(
                    "ZONE_GEOMETRY_VERSION_NOT_FOUND", "Zone geometry version was not found.")));
            }
        });

        return endpoints;
    }
}
