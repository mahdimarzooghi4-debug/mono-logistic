using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoMissionDispatchAttemptHistoryEndpoints
{
    public static IEndpointRouteBuilder MapMonoMissionDispatchAttemptHistoryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/missions/{missionId}/dispatch-attempts", async (
                string missionId,
                PostgresMissionDispatchAttemptHistoryService history,
                PostgresMissionOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                if (!MissionHistoryInputValidator.IsValidMissionId(missionId))
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Mission ID is invalid."));

                var result = await history.FindAsync(missionId, cancellationToken);
                if (result is null)
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("MISSION_NOT_FOUND", "The Mission was not found.")));

                await readAudit.RecordAsync(
                    PostgresMissionOperationsReadAuditService.MissionDispatchAttemptHistory,
                    missionId, result.Attempts.Count, cancellationToken);
                return Results.Ok(result);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }
}
