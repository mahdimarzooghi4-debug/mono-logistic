using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class CustomerServiceConfigurationInspectionEndpoints
{
    public static IEndpointRouteBuilder MapCustomerServiceConfigurationInspectionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        const string route =
            "/operations/v1/customers/{customerId}/services/{serviceCode}";

        endpoints.MapGet(route + "/configurations", async (
                string customerId,
                string serviceCode,
                [FromQuery(Name = "limit")] int? limit,
                CustomerServiceConfigurationInspectionService inspection,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var versions = await inspection.ListVersionsAsync(
                        customerId, serviceCode, limit ?? 50, cancellationToken);
                    return Results.Ok(versions);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer, service, or limit is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet(route + "/configuration-audit", async (
                string customerId,
                string serviceCode,
                [FromQuery(Name = "limit")] int? limit,
                CustomerServiceConfigurationInspectionService inspection,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var entries = await inspection.ListAuditHistoryAsync(
                        customerId, serviceCode, limit ?? 50, cancellationToken);
                    return Results.Ok(entries);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer, service, or limit is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}
