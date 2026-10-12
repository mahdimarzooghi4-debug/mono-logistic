using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoZoneGeometryReviewEndpoints
{
    public static IEndpointRouteBuilder MapMonoZoneGeometryReviewEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/operations/v1/zones/{zoneId}/geometry/{geometryVersion}/reviews")
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        group.MapGet("", async (
            string zoneId,
            string geometryVersion,
            [FromServices] PostgresZoneGeometryReviewService reviews,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var entries = await reviews.ListAsync(zoneId, geometryVersion, cancellationToken);
                return entries is null
                    ? Results.NotFound(new ApiErrorResponse(
                        new ApiError("ZONE_GEOMETRY_VERSION_NOT_FOUND", "Zone geometry version was not found.")))
                    : Results.Ok(new ZoneGeometryReviewListResponse(entries));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(
                    new ApiError("INVALID_REQUEST", "Zone ID or geometry version is invalid.")));
            }
        });

        group.MapPost("", async (
            string zoneId,
            string geometryVersion,
            RecordZoneGeometryReviewRequest request,
            [FromServices] PostgresZoneGeometryReviewService reviews,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var entry = await reviews.RecordAsync(zoneId, geometryVersion,
                    request.ExpectedSnapshotSha256 ?? string.Empty,
                    request.Decision ?? string.Empty,
                    request.Reason ?? string.Empty, cancellationToken);
                return Results.Created(
                    $"/operations/v1/zones/{entry.ZoneId}/geometry/{entry.GeometryVersion}/reviews/{entry.ReviewId}",
                    entry);
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_REQUEST", "Snapshot hash, decision, or review reason is invalid.")));
            }
            catch (ZoneGeometrySnapshotNotFoundException)
            {
                return Results.NotFound(new ApiErrorResponse(new ApiError(
                    "ZONE_GEOMETRY_VERSION_NOT_FOUND", "Zone geometry version was not found.")));
            }
            catch (ZoneGeometrySnapshotHashConflictException)
            {
                return Results.Conflict(new ApiErrorResponse(new ApiError(
                    "ZONE_GEOMETRY_SNAPSHOT_CHANGED", "Review must reference the stored snapshot hash.")));
            }
            catch (ZoneGeometrySnapshotIsActiveException)
            {
                return Results.Conflict(new ApiErrorResponse(new ApiError(
                    "ZONE_GEOMETRY_MUST_BE_DEACTIVATED",
                    "Deactivate this geometry before recording a REJECT review.")));
            }
        });

        return endpoints;
    }
}

public sealed record RecordZoneGeometryReviewRequest(
    string? ExpectedSnapshotSha256,
    string? Decision,
    string? Reason);

public sealed record ZoneGeometryReviewListResponse(
    IReadOnlyList<ZoneGeometryReviewEntry> Entries);
