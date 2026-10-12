using Mono.Api.Security;
using Mono.Api.Services;

namespace Mono.Api.Api;

public static class MonoZoneGeometryCatalogEndpoints
{
    public static IEndpointRouteBuilder MapMonoZoneGeometryCatalogEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/zones/geometry-catalog", async (
            string? kind,
            int? pageSize,
            string? cursor,
            MonoZoneGeometryCatalogService catalog,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await catalog.ListAsync(
                    kind, pageSize, cursor, cancellationToken));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_CATALOG_QUERY",
                    "Kind, pageSize, or cursor is invalid.")));
            }
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}
