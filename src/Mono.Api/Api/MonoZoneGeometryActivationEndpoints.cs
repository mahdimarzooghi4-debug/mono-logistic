using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoZoneGeometryActivationEndpoints
{
    public static IEndpointRouteBuilder MapMonoZoneGeometryActivationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/operations/v1/zones/{zoneId}/geometry")
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        group.MapPost("/{geometryVersion}/activate", async (
            string zoneId,
            string geometryVersion,
            ZoneGeometryActivateRequest request,
            PostgresZoneGeometryActivationService activations,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await activations.ActivateAsync(
                    zoneId, geometryVersion,
                    request.ExpectedSnapshotSha256 ?? string.Empty,
                    request.Reason ?? string.Empty,
                    cancellationToken));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_REQUEST", "Zone, version, snapshot hash, or reason is invalid.")));
            }
            catch (ZoneGeometryActivationSnapshotNotFoundException)
            {
                return Results.NotFound(new ApiErrorResponse(new ApiError(
                    "ZONE_GEOMETRY_VERSION_NOT_FOUND", "Zone geometry version was not found.")));
            }
            catch (ZoneGeometryActivationHashConflictException)
            {
                return Results.Conflict(new ApiErrorResponse(new ApiError(
                    "ZONE_GEOMETRY_SNAPSHOT_CHANGED", "Activation must reference the stored snapshot hash.")));
            }
            catch (ZoneGeometryActivationReviewRequiredException)
            {
                return Results.Conflict(new ApiErrorResponse(new ApiError(
                    "ZONE_GEOMETRY_APPROVAL_REQUIRED", "The latest review for this snapshot must be APPROVE.")));
            }
            catch (ZoneGeometryActivationStateConflictException ex)
            {
                return Results.Conflict(new ApiErrorResponse(new ApiError(
                    "ZONE_GEOMETRY_ACTIVATION_CONFLICT", ex.Message)));
            }
        });

        group.MapPost("/{geometryVersion}/deactivate", async (
            string zoneId,
            string geometryVersion,
            ZoneGeometryDeactivateRequest request,
            PostgresZoneGeometryActivationService activations,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await activations.DeactivateAsync(
                    zoneId, geometryVersion, request.Reason ?? string.Empty, cancellationToken);
                return Results.NoContent();
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_REQUEST", "Zone, version, or reason is invalid.")));
            }
            catch (ZoneGeometryActivationSnapshotNotFoundException)
            {
                return Results.NotFound(new ApiErrorResponse(new ApiError(
                    "ZONE_GEOMETRY_ACTIVATION_NOT_FOUND", "No matching active geometry was found.")));
            }
            catch (ZoneGeometryActivationStateConflictException ex)
            {
                return Results.Conflict(new ApiErrorResponse(new ApiError(
                    "ZONE_GEOMETRY_ACTIVATION_CONFLICT", ex.Message)));
            }
        });

        endpoints.MapGet("/operations/v1/zones/{zoneId}/geometry-activation", async (
            string zoneId,
            PostgresZoneGeometryActivationService activations,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var current = await activations.GetCurrentAsync(zoneId, cancellationToken);
                var history = await activations.ListHistoryAsync(zoneId, cancellationToken);
                return Results.Ok(new ZoneGeometryActivationDetail(current, history));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_REQUEST", "Zone ID is invalid.")));
            }
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}

public sealed record ZoneGeometryActivateRequest(string? ExpectedSnapshotSha256, string? Reason);
public sealed record ZoneGeometryDeactivateRequest(string? Reason);
public sealed record ZoneGeometryActivationDetail(
    ZoneGeometryActivationState? Current,
    IReadOnlyList<ZoneGeometryActivationEvent> History);
