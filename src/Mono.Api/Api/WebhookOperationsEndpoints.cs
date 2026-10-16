using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class WebhookOperationsEndpoints
{
    public static IEndpointRouteBuilder MapWebhookOperationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/webhook-deliveries/summary", async Task<IResult> (
                PostgresWebhookOperationsReadService service,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var summary = await service.GetQueueSummaryAsync(timeProvider.GetUtcNow(), cancellationToken);
                return Results.Ok(summary);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }
}
