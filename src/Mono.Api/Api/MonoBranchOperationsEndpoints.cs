using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoBranchOperationsEndpoints
{
    public static IEndpointRouteBuilder MapMonoBranchOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/operations/v1/customers/{customerId}/branches", async (
                string customerId,
                CreateMonoBranchRequest request,
                MonoBranchOperationsService branches,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var branch = await branches.CreateForActiveCustomerAsync(
                        customerId,
                        request.Name ?? string.Empty,
                        request.Address,
                        request.Latitude,
                        request.Longitude,
                        request.ZoneId,
                        request.OperatingHours,
                        request.Reason ?? string.Empty,
                        cancellationToken);
                    return Results.Created(
                        $"/operations/v1/customers/{customerId}/branches", branch);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST",
                            "Branch fields, coordinates, operating hours, or reason are invalid.")));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("CUSTOMER_NOT_FOUND", "Customer was not found.")));
                }
                catch (InvalidOperationException)
                {
                    return Results.Conflict(new ApiErrorResponse(
                        new ApiError("CUSTOMER_CONFLICT", "A branch can only be added to an active customer.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}

public sealed record CreateMonoBranchRequest(
    string? Name,
    string? Address,
    double? Latitude,
    double? Longitude,
    string? ZoneId,
    JsonElement? OperatingHours,
    string? Reason);
