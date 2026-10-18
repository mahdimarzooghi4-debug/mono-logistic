using Microsoft.AspNetCore.Mvc;
using Mono.Api.Domain;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MissionCourierOfferExpiryWorkerStatusEndpoints
{
    public static IEndpointRouteBuilder MapMissionCourierOfferExpiryWorkerStatusEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/dispatch/offer-expiry-worker/status",
            async Task<IResult> (HttpContext context, MissionCourierOfferExpiryWorkerStatus status,
                PostgresMissionOperationsReadAuditService audit, TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var snapshot = status.GetSnapshot();
                await audit.RecordAsync(PostgresMissionOperationsReadAuditService.MissionWorkerStatus,
                    null, 1, cancellationToken);
                return Results.Ok(snapshot);
            }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }
}
