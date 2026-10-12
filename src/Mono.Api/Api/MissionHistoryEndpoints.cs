using Microsoft.AspNetCore.Mvc;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MissionHistoryEndpoints
{
    public static IEndpointRouteBuilder MapMissionHistoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/missions", async (
                [FromQuery(Name = "page_size")] int? pageSize,
                string? cursor,
                PostgresMissionHistoryRepository missions,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 50;
                if (size is < 1 or > 100)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "page_size must be between 1 and 100.")));
                }

                if (cursor is not null && !MissionHistoryInputValidator.IsValidMissionId(cursor))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The cursor is invalid for this customer.")));
                }

                var page = await missions.ListAsync(size, cursor, cancellationToken);
                if (page is null)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The cursor is invalid for this customer.")));
                }

                return Results.Ok(page);
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsRead);

        return endpoints;
    }
}
