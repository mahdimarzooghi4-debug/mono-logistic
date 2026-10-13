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

        endpoints.MapGet("/operations/v1/dispatch/queue", async (
                [FromQuery(Name = "page_size")] int? pageSize,
                [FromQuery(Name = "has_offer")] bool? hasOffer,
                [FromQuery(Name = "deadline_passed")] bool? deadlinePassed,
                [FromQuery(Name = "delivery_before")] DateTimeOffset? deliveryBefore,
                [FromQuery(Name = "last_attempt_before")] DateTimeOffset? lastAttemptBefore,
                [FromQuery(Name = "min_attempt_count")] int? minAttemptCount,
                [FromQuery(Name = "max_attempt_count")] int? maxAttemptCount,
                [FromQuery(Name = "latest_action")] string? latestAction,
                string? query,
                string? cursor,
                PostgresMissionDispatchQueueService dispatchQueue,
                PostgresMissionOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 50;
                if (size is < 1 or > 100 ||
                    !MissionOperationsInputValidator.IsValidQuery(query) ||
                    !MissionOperationsInputValidator.IsValidCursor(cursor) ||
                    latestAction is not (null or "OFFERED" or "ACCEPTED" or "DECLINED" or "WITHDRAWN" or
                        "EXPIRED" or "RELEASED" or "CANCELLED" or "COMPLETED") ||
                    (minAttemptCount.HasValue && minAttemptCount.Value < 0) ||
                    (maxAttemptCount.HasValue && maxAttemptCount.Value < 0) ||
                    (minAttemptCount.HasValue && maxAttemptCount.HasValue &&
                     minAttemptCount.Value > maxAttemptCount.Value))
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Dispatch queue filters are invalid.")));

                var page = await dispatchQueue.ListAsync(
                    size, hasOffer, query, cursor, cancellationToken, deadlinePassed, latestAction,
                    deliveryBefore, minAttemptCount, maxAttemptCount, lastAttemptBefore);
                if (page is null)
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The dispatch queue cursor is invalid for these filters.")));

                await readAudit.RecordAsync(
                    PostgresMissionOperationsReadAuditService.MissionDispatchQueue,
                    null, page.Items.Count, cancellationToken);
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

        endpoints.MapGet("/operations/v1/mission-read-audit", async (
                [FromQuery(Name = "page_size")] int? pageSize,
                [FromQuery(Name = "mission_id")] string? missionId,
                [FromQuery(Name = "actor_id")] string? actorId,
                [FromQuery(Name = "created_after")] DateTimeOffset? createdAfter,
                [FromQuery(Name = "created_before")] DateTimeOffset? createdBefore,
                string? cursor,
                PostgresMissionOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 50;
                if (size is < 1 or > 100 ||
                    (missionId is not null && !MissionHistoryInputValidator.IsValidMissionId(missionId)) ||
                    !PostgresMissionOperationsReadAuditService.IsValidActorId(actorId) ||
                    !MissionOperationsInputValidator.IsValidDateRange(createdAfter, createdBefore) ||
                    !PostgresMissionOperationsReadAuditService.IsValidAuditCursor(cursor))
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Mission read audit filters are invalid.")));

                var page = await readAudit.ListAsync(
                    size, missionId, actorId, createdAfter, createdBefore, cursor, cancellationToken);
                return page is null
                    ? Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The audit cursor is invalid for these filters.")))
                    : Results.Ok(page);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/delivery-requests", async (
                [FromQuery(Name = "page_size")] int? pageSize,
                string? status,
                string? query,
                [FromQuery(Name = "created_after")] DateTimeOffset? createdAfter,
                [FromQuery(Name = "created_before")] DateTimeOffset? createdBefore,
                [FromQuery(Name = "customer_id")] string? customerId,
                [FromQuery(Name = "branch_id")] string? branchId,
                [FromQuery(Name = "has_mission")] bool? hasMission,
                string? cursor,
                PostgresDeliveryRequestOperationsService deliveryRequests,
                PostgresDeliveryRequestOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 50;
                if (size is < 1 or > 100 ||
                    !DeliveryRequestOperationsInputValidator.IsValidStatus(status) ||
                    !DeliveryRequestOperationsInputValidator.IsValidQuery(query) ||
                    !DeliveryRequestOperationsInputValidator.IsValidDateRange(createdAfter, createdBefore) ||
                    !DeliveryRequestOperationsInputValidator.IsValidCustomerId(customerId) ||
                    !DeliveryRequestOperationsInputValidator.IsValidBranchId(branchId) ||
                    !DeliveryRequestOperationsInputValidator.IsValidCursor(cursor))
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Delivery Request search filters are invalid.")));
 
                var page = await deliveryRequests.ListAsync(
                    size, status, query, createdAfter, createdBefore,
                    customerId, branchId, hasMission, cursor, cancellationToken);
                if (page is null)
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The Delivery Request cursor is invalid for these filters.")));

                await readAudit.RecordAsync(
                    PostgresDeliveryRequestOperationsReadAuditService.Search,
                    null, page.Items.Count, cancellationToken);
                return Results.Ok(page);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/delivery-requests/{deliveryRequestId}", async (
                string deliveryRequestId,
                PostgresDeliveryRequestOperationsService deliveryRequests,
                PostgresDeliveryRequestOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                if (!DeliveryRequestOperationsInputValidator.IsValidCursor(deliveryRequestId))
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "The Delivery Request ID is invalid.")));

                var request = await deliveryRequests.FindByIdAsync(deliveryRequestId, cancellationToken);
                await readAudit.RecordAsync(
                    PostgresDeliveryRequestOperationsReadAuditService.Detail,
                    deliveryRequestId, request is null ? 0 : 1, cancellationToken);
                return request is null
                    ? Results.NotFound(new ApiErrorResponse(
                        new ApiError("DELIVERY_REQUEST_NOT_FOUND", "The Delivery Request was not found.")))
                    : Results.Ok(request);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/delivery-request-read-audit", async (
                [FromQuery(Name = "page_size")] int? pageSize,
                [FromQuery(Name = "delivery_request_id")] string? deliveryRequestId,
                [FromQuery(Name = "actor_id")] string? actorId,
                [FromQuery(Name = "created_after")] DateTimeOffset? createdAfter,
                [FromQuery(Name = "created_before")] DateTimeOffset? createdBefore,
                string? cursor,
                PostgresDeliveryRequestOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 50;
                if (size is < 1 or > 100 ||
                    (deliveryRequestId is not null &&
                     !DeliveryRequestInputValidator.IsValidDeliveryRequestId(deliveryRequestId)) ||
                    !PostgresDeliveryRequestOperationsReadAuditService.IsValidActorId(actorId) ||
                    !DeliveryRequestOperationsInputValidator.IsValidDateRange(createdAfter, createdBefore))
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Delivery Request audit filters are invalid.")));
                if (!PostgresDeliveryRequestOperationsReadAuditService.IsValidAuditCursor(cursor))
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The Delivery Request audit cursor is invalid.")));

                var page = await readAudit.ListAsync(
                    size, deliveryRequestId, actorId, createdAfter, createdBefore, cursor, cancellationToken);
                return page is null
                    ? Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The Delivery Request audit cursor is invalid for these filters.")))
                    : Results.Ok(page);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}
