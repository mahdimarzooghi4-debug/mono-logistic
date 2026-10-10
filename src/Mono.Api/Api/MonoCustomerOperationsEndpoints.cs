using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoCustomerOperationsEndpoints
{
    public static IEndpointRouteBuilder MapMonoCustomerOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/customers", async (
                [FromQuery(Name = "page_size")] int? pageSize,
                string? cursor,
                string? status,
                string? search,
                MonoCustomerOperationsService customers,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 50;
                if (size is < 1 or > 100)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "page_size must be between 1 and 100.")));
                }

                var normalizedStatus = status?.Trim().ToUpperInvariant();
                if (normalizedStatus is not null && normalizedStatus is not ("PENDING" or "ACTIVE"))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "status must be PENDING or ACTIVE.")));
                }

                if (search is not null &&
                    (string.IsNullOrWhiteSpace(search) || search.Trim().Length > 128))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "search must contain 1 to 128 characters.")));
                }

                try
                {
                    var page = await customers.ListAsync(
                        size, cursor, normalizedStatus, search, cancellationToken);
                    return page is null
                        ? Results.BadRequest(new ApiErrorResponse(
                            new ApiError("INVALID_CURSOR", "The cursor is invalid.")))
                        : Results.Ok(page);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The cursor is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/customers/{customerId}", async (
                string customerId,
                MonoCustomerOperationsService customers,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var customer = await customers.FindAsync(customerId, cancellationToken);
                    return customer is null
                        ? Results.NotFound(new ApiErrorResponse(
                            new ApiError("CUSTOMER_NOT_FOUND", "Customer was not found.")))
                        : Results.Ok(customer);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer ID is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/customers/{customerId}/audit", async (
                string customerId,
                MonoCustomerOperationsService customers,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var entries = await customers.ListAuditAsync(customerId, cancellationToken);
                    return entries is null
                        ? Results.NotFound(new ApiErrorResponse(
                            new ApiError("CUSTOMER_NOT_FOUND", "Customer was not found.")))
                        : Results.Ok(new MonoCustomerAuditListResponse(entries));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer ID is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

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

public sealed record MonoCustomerAuditListResponse(
    IReadOnlyList<MonoCustomerAuditSummary> Entries);

public sealed record CreateMonoCustomerRequest(
    string? LegalName,
    string? DisplayName,
    string? CustomerType,
    string? DefaultChannel,
    string? DefaultCurrency,
    string? Reason);

public sealed record ActivateMonoCustomerRequest(string? Reason);
