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
        endpoints.MapGet("/operations/v1/customers/{customerId}/branches", async (
                string customerId,
                MonoBranchOperationsService branches,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var result = await branches.ListForCustomerAsync(customerId, cancellationToken);
                    return Results.Ok(new ManagedBranchListResponse(result));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer ID is invalid.")));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("CUSTOMER_NOT_FOUND", "Customer was not found.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

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

        endpoints.MapPut("/operations/v1/customers/{customerId}/branches/{branchId}", async (
                string customerId,
                string branchId,
                UpdateMonoBranchRequest request,
                MonoBranchOperationsService branches,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var branch = await branches.UpdateForActiveCustomerAsync(
                        customerId,
                        branchId,
                        request.Name ?? string.Empty,
                        request.Address,
                        request.Latitude,
                        request.Longitude,
                        request.ZoneId,
                        request.OperatingHours,
                        request.Reason ?? string.Empty,
                        cancellationToken);
                    return Results.Ok(branch);
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
                        new ApiError("BRANCH_NOT_FOUND", "Branch was not found for this customer.")));
                }
                catch (InvalidOperationException)
                {
                    return Results.Conflict(new ApiErrorResponse(
                        new ApiError("BRANCH_CONFLICT", "Only an active branch of an active customer can be updated.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet(
            "/operations/v1/customers/{customerId}/branches/{branchId}",
            async (
                string customerId,
                string branchId,
                MonoBranchInspectionService inspection,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var branch = await inspection.FindAsync(customerId, branchId, cancellationToken);
                    return branch is null
                        ? Results.NotFound(new ApiErrorResponse(
                            new ApiError("BRANCH_NOT_FOUND", "Branch was not found for this customer.")))
                        : Results.Ok(branch);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer or branch ID is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet(
            "/operations/v1/customers/{customerId}/branches/{branchId}/audit",
            async (
                string customerId,
                string branchId,
                [FromQuery(Name = "page_size")] int? pageSize,
                string? cursor,
                MonoBranchInspectionService inspection,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 50;
                if (size is < 1 or > 100)
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "page_size must be between 1 and 100.")));
                try
                {
                    var page = await inspection.ListAuditAsync(
                        customerId, branchId, size, cursor, cancellationToken);
                    return page is null
                        ? Results.NotFound(new ApiErrorResponse(
                            new ApiError("BRANCH_NOT_FOUND", "Branch was not found for this customer.")))
                        : Results.Ok(page);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer or branch ID is invalid.")));
                }
                catch (BranchAuditCursorException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The audit cursor is invalid for this branch.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}

public sealed record ManagedBranchListResponse(IReadOnlyList<ManagedBranchSummary> Branches);

public sealed record UpdateMonoBranchRequest(
    string? Name,
    string? Address,
    double? Latitude,
    double? Longitude,
    string? ZoneId,
    JsonElement? OperatingHours,
    string? Reason);

public sealed record CreateMonoBranchRequest(
    string? Name,
    string? Address,
    double? Latitude,
    double? Longitude,
    string? ZoneId,
    JsonElement? OperatingHours,
    string? Reason);
