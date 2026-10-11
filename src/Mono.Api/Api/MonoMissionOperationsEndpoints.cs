using Microsoft.AspNetCore.Mvc;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoMissionOperationsEndpoints
{
    public static IEndpointRouteBuilder MapMonoMissionOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/missions", async (
                [FromQuery(Name = "page_size")] int? pageSize,
                string? status,
                string? query,
                [FromQuery(Name = "created_after")] DateTimeOffset? createdAfter,
                [FromQuery(Name = "created_before")] DateTimeOffset? createdBefore,
                string? cursor,
                PostgresMissionOperationsService missions,
                PostgresMissionOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 50;
                if (size is < 1 or > 100 ||
                    !MissionOperationsInputValidator.IsValidStatus(status) ||
                    !MissionOperationsInputValidator.IsValidQuery(query) ||
                    !MissionOperationsInputValidator.IsValidDateRange(createdAfter, createdBefore))
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Mission search filters are invalid.")));
                if (!MissionOperationsInputValidator.IsValidCursor(cursor))
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The Mission cursor is invalid.")));

                var page = await missions.ListAsync(
                    size, status, query, createdAfter, createdBefore, cursor, cancellationToken);
                if (page is null)
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The Mission cursor is invalid for these filters.")));

                await readAudit.RecordAsync(
                    PostgresMissionOperationsReadAuditService.MissionSearch, null, page.Items.Count, cancellationToken);
                return Results.Ok(page);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/missions/{missionId}", async (
                string missionId,
                PostgresMissionOperationsService missions,
                PostgresMissionOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                if (!MissionHistoryInputValidator.IsValidMissionId(missionId))
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "The Mission ID is invalid.")));

                var mission = await missions.FindByIdAsync(missionId, cancellationToken);
                await readAudit.RecordAsync(
                    PostgresMissionOperationsReadAuditService.MissionDetail,
                    missionId, mission is null ? 0 : 1, cancellationToken);
                return mission is null
                    ? Results.NotFound(new ApiErrorResponse(
                        new ApiError("MISSION_NOT_FOUND", "The Mission was not found.")))
                    : Results.Ok(mission);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/missions/{missionId}/timeline", async (
                string missionId,
                PostgresMissionOperationsService missions,
                PostgresMissionOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                if (!MissionHistoryInputValidator.IsValidMissionId(missionId))
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "The Mission ID is invalid.")));

                var timeline = await missions.FindTimelineAsync(missionId, cancellationToken);
                await readAudit.RecordAsync(
                    PostgresMissionOperationsReadAuditService.MissionTimeline,
                    missionId, timeline?.Events.Count ?? 0, cancellationToken);
                return timeline is null
                    ? Results.NotFound(new ApiErrorResponse(
                        new ApiError("MISSION_NOT_FOUND", "The Mission was not found.")))
                    : Results.Ok(timeline);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}
