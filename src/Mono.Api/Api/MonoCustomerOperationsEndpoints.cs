using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoCustomerOperationsEndpoints
{
    public static IEndpointRouteBuilder MapMonoCustomerOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/operations/v1/customers", async (
                CreateMonoCustomerRequest request,
                MonoCustomerOperationsService customers,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var customer = await customers.CreatePendingAsync(
                        request.LegalName ?? string.Empty,
                        request.DisplayName,
                        request.CustomerType,
                        request.DefaultChannel,
                        request.DefaultCurrency,
                        request.Reason ?? string.Empty,
                        cancellationToken);
                    return Results.Created(
                        $"/operations/v1/customers/{customer.CustomerId}", customer);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer details or reason are invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/customers/{customerId}/activate", async (
                string customerId,
                ActivateMonoCustomerRequest request,
                MonoCustomerOperationsService customers,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    await customers.ActivateAsync(
                        customerId, request.Reason ?? string.Empty, cancellationToken);
                    return Results.NoContent();
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer ID or reason is invalid.")));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("CUSTOMER_NOT_FOUND", "Customer was not found.")));
                }
                catch (InvalidOperationException)
                {
                    return Results.Conflict(new ApiErrorResponse(
                        new ApiError("CUSTOMER_CONFLICT", "Only a pending customer can be activated.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}

public sealed record CreateMonoCustomerRequest(
    string? LegalName,
    string? DisplayName,
    string? CustomerType,
    string? DefaultChannel,
    string? DefaultCurrency,
    string? Reason);

public sealed record ActivateMonoCustomerRequest(string? Reason);
