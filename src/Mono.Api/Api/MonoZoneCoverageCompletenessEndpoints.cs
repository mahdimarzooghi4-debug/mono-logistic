using Mono.Api.Security;
using Mono.Api.Services;

namespace Mono.Api.Api;

public static class MonoZoneCoverageCompletenessEndpoints
{
    public static IEndpointRouteBuilder MapMonoZoneCoverageCompletenessEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/operations/v1/zones/geometry-coverage-check", async (
            ZoneGeometryCoverageCheckRequest request,
            MonoZoneCoverageCompletenessService coverage,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await coverage.CheckAsync(
                    request.Parent, request.Children, cancellationToken));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_GEOMETRY_COVERAGE_REQUEST",
                    "A parent and 1–50 unique child snapshots of its hierarchy level are required.")));
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

public sealed record ZoneGeometryCoverageCheckRequest(
    ZoneGeometrySnapshotReference? Parent,
    IReadOnlyList<ZoneGeometrySnapshotReference>? Children);
