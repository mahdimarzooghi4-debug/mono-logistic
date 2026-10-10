using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoPolicyDraftOperationsEndpoints
{
    public static IEndpointRouteBuilder MapMonoPolicyDraftOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/operations/v1/policies",
            async (
                [FromQuery(Name = "page_size")] int? pageSize,
                string? cursor,
                [FromServices] MonoPolicyDraftOperationsService policies,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var page = await policies.ListAsync(pageSize ?? 50, cursor, cancellationToken);
                    return Results.Ok(page);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Page size or policy cursor is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost(
            "/operations/v1/policies",
            async (
                CreatePolicyDraftRequest request,
                [FromServices] MonoPolicyDraftOperationsService policies,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var draft = await policies.CreateAsync(new CreatePolicyDraftInput(
                        request.PolicyType ?? string.Empty,
                        request.Scope ?? string.Empty,
                        request.ScopeId,
                        request.EnforcementType ?? string.Empty,
                        request.Priority,
                        request.EffectiveFrom,
                        request.EffectiveTo,
                        request.Definition,
                        request.Reason ?? string.Empty), cancellationToken);
                    return Results.Created(
                        $"/operations/v1/policies/{draft.PolicyId}/versions/{draft.VersionNumber}", draft);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Policy type, scope, definition, dates, or reason is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet(
            "/operations/v1/policies/{policyId}",
            async (
                string policyId,
                [FromServices] MonoPolicyDraftOperationsService policies,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var policy = await policies.GetAsync(policyId, cancellationToken);
                    return policy is null
                        ? Results.NotFound(new ApiErrorResponse(
                            new ApiError("POLICY_NOT_FOUND", "Policy was not found.")))
                        : Results.Ok(policy);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Policy ID is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost(
            "/operations/v1/policies/{policyId}/versions",
            async (
                string policyId,
                CreatePolicyVersionRequest request,
                [FromServices] MonoPolicyDraftOperationsService policies,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var version = await policies.CreateVersionAsync(policyId, new CreatePolicyVersionInput(
                        request.EnforcementType ?? string.Empty,
                        request.Priority,
                        request.EffectiveFrom,
                        request.EffectiveTo,
                        request.Definition,
                        request.Reason ?? string.Empty), cancellationToken);
                    return Results.Created(
                        $"/operations/v1/policies/{policyId}/versions/{version.VersionNumber}", version);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Policy ID, definition, dates, or reason is invalid.")));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("POLICY_NOT_FOUND", "Policy was not found.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet(
            "/operations/v1/policies/{policyId}/audit",
            async (
                string policyId,
                [FromServices] MonoPolicyDraftOperationsService policies,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var audit = await policies.ListAuditAsync(policyId, cancellationToken);
                    return audit is null
                        ? Results.NotFound(new ApiErrorResponse(
                            new ApiError("POLICY_NOT_FOUND", "Policy was not found.")))
                        : Results.Ok(new PolicyAuditListResponse(audit));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Policy ID is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}

public sealed record CreatePolicyDraftRequest(
    string? PolicyType,
    string? Scope,
    string? ScopeId,
    string? EnforcementType,
    int Priority,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    System.Text.Json.JsonElement Definition,
    string? Reason);

public sealed record CreatePolicyVersionRequest(
    string? EnforcementType,
    int Priority,
    DateTimeOffset? EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    System.Text.Json.JsonElement Definition,
    string? Reason);

public sealed record PolicyAuditListResponse(IReadOnlyList<PolicyAuditSummary> Entries);
