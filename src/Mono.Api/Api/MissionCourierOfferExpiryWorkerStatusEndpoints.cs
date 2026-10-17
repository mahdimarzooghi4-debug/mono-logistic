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
            (HttpContext context, MissionCourierOfferExpiryWorkerStatus status) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(status.GetSnapshot());
            }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }
}
