using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoBranchExternalIdentifierOperationsEndpoints
{
    public static IEndpointRouteBuilder MapMonoBranchExternalIdentifierOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/operations/v1/customers/{customerId}/branches/{branchId}/external-identifiers",
            async (
                string customerId,
                string branchId,
                [FromServices] MonoBranchExternalIdentifierOperationsService identifiers,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var entries = await identifiers.ListAsync(customerId, branchId, cancellationToken);
                    return entries is null
                        ? Results.NotFound(new ApiErrorResponse(
                            new ApiError("BRANCH_NOT_FOUND", "Branch was not found for this customer.")))
                        : Results.Ok(new BranchExternalIdentifierListResponse(entries));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer or branch ID is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost(
            "/operations/v1/customers/{customerId}/branches/{branchId}/external-identifiers",
            async (
                string customerId,
                string branchId,
                AddBranchExternalIdentifierRequest request,
                [FromServices] MonoBranchExternalIdentifierOperationsService identifiers,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var entry = await identifiers.AddAsync(
                        customerId,
                        branchId,
                        request.Namespace ?? string.Empty,
                        request.ExternalId ?? string.Empty,
                        request.Reason ?? string.Empty,
                        cancellationToken);
                    return Results.Created(
                        $"/operations/v1/customers/{customerId}/branches/{branchId}/external-identifiers",
                        entry);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST",
                            "Customer ID, branch ID, namespace, external ID, or reason is invalid.")));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("BRANCH_NOT_FOUND", "Branch was not found for this customer.")));
                }
                catch (InvalidOperationException)
                {
                    return Results.Conflict(new ApiErrorResponse(
                        new ApiError("BRANCH_CONFLICT",
                            "An identifier can only be added to an active branch of an active customer.")));
                }
                catch (BranchExternalIdentifierConflictException)
                {
                    return Results.Conflict(new ApiErrorResponse(
                        new ApiError("BRANCH_EXTERNAL_IDENTIFIER_CONFLICT",
                            "The external identifier is already assigned to a branch in this customer and namespace.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost(
            "/operations/v1/customers/{customerId}/branches/{branchId}/external-identifiers/{identifierId}/retire",
            async (
                string customerId,
                string branchId,
                string identifierId,
                RetireBranchExternalIdentifierRequest request,
                [FromServices] MonoBranchExternalIdentifierOperationsService identifiers,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var retired = await identifiers.RetireAsync(
                        customerId, branchId, identifierId, request.Reason ?? string.Empty, cancellationToken);
                    return retired
                        ? Results.NoContent()
                        : Results.NotFound(new ApiErrorResponse(
                            new ApiError("BRANCH_EXTERNAL_IDENTIFIER_NOT_FOUND",
                                "External identifier was not found for this branch.")));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST",
                            "Customer ID, branch ID, external identifier ID, or reason is invalid.")));
                }
                catch (BranchExternalIdentifierConflictException)
                {
                    return Results.Conflict(new ApiErrorResponse(
                        new ApiError("BRANCH_EXTERNAL_IDENTIFIER_CONFLICT",
                            "External identifier is already retired or conflicts with an active mapping.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet(
            "/operations/v1/customers/{customerId}/branches/{branchId}/external-identifiers/audit",
            async (
                string customerId,
                string branchId,
                [FromServices] MonoBranchExternalIdentifierOperationsService identifiers,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var entries = await identifiers.ListAuditAsync(customerId, branchId, cancellationToken);
                    return entries is null
                        ? Results.NotFound(new ApiErrorResponse(
                            new ApiError("BRANCH_NOT_FOUND", "Branch was not found for this customer.")))
                        : Results.Ok(new BranchExternalIdentifierAuditListResponse(entries));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer or branch ID is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}

public sealed record BranchExternalIdentifierListResponse(
    IReadOnlyList<BranchExternalIdentifierSummary> Identifiers);

public sealed record BranchExternalIdentifierAuditListResponse(
    IReadOnlyList<BranchExternalIdentifierAuditSummary> Entries);

public sealed record AddBranchExternalIdentifierRequest(
    string? Namespace,
    string? ExternalId,
    string? Reason);

public sealed record RetireBranchExternalIdentifierRequest(string? Reason);
