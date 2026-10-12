using Mono.Api.Security;
using Mono.Api.Services;

namespace Mono.Api.Api;

public static class MonoZoneGeometryQualityEndpoints
{
    public static IEndpointRouteBuilder MapMonoZoneGeometryQualityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/operations/v1/zones/geometry-quality-check", async (
            ZoneGeometryQualityCheckRequest request,
            MonoZoneGeometryQualityService quality,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await quality.CheckAsync(
                    request.Kind ?? string.Empty, request.Snapshots, cancellationToken));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_GEOMETRY_QUALITY_REQUEST",
                    "Kind must be valid and 2–50 unique snapshots of that kind are required.")));
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

public sealed record ZoneGeometryQualityCheckRequest(
    string? Kind,
    IReadOnlyList<ZoneGeometrySnapshotReference>? Snapshots);
