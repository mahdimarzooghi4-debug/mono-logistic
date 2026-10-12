using Mono.Api.Security;

namespace Mono.Api.Api;

/// <summary>Minimal authenticated courier self-service surface.</summary>
public static class MonoCourierSessionEndpoints
{
    public static IEndpointRouteBuilder MapMonoCourierSessionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/courier/v1/session", (IMonoCourierContext courier) =>
            Results.Ok(new
            {
                courier_id = courier.CourierId,
                identity_domain = MonoCourierDefaults.IdentityDomainValue
            }))
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);
        return endpoints;
    }
}
