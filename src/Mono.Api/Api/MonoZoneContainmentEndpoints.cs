using Mono.Api.Security;
using Mono.Api.Services;

namespace Mono.Api.Api;

public static class MonoZoneContainmentEndpoints
{
    public static IEndpointRouteBuilder MapMonoZoneContainmentEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/operations/v1/zones/geometry-containment-check", async (
            ZoneGeometryContainmentCheckRequest request,
            MonoZoneGeometryQualityService quality,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await quality.CheckContainmentAsync(
                    request.ParentKind ?? string.Empty,
                    request.Parent,
                    request.ChildKind ?? string.Empty,
                    request.Children,
                    cancellationToken));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_GEOMETRY_CONTAINMENT_REQUEST",
                    "Only valid City→OperatingZone or OperatingZone→MicroZone snapshot sets are accepted.")));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new ApiErrorResponse(new ApiError(
                    "ZONE_GEOMETRY_SNAPSHOT_NOT_FOUND", "A requested zone geometry snapshot was not found.")));
            }
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}

public sealed record ZoneGeometryContainmentCheckRequest(
    string? ParentKind,
    ZoneGeometrySnapshotReference? Parent,
    string? ChildKind,
    IReadOnlyList<ZoneGeometrySnapshotReference>? Children);
