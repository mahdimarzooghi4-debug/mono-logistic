using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoHardPolicyEvaluationEndpoints
{
    public static IEndpointRouteBuilder MapMonoHardPolicyEvaluationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/operations/v1/policies/modes/eligible",
            async (
                HardPolicyEvaluationRequest request,
                [FromServices] MonoHardPolicyEvaluationService evaluator,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var context = new EffectivePolicyContext(
                        request.ServiceId, request.CustomerId, request.ContractId,
                        request.BranchId, request.ZoneIds, request.MissionId,
                        request.RuntimeId, request.RequestedAt ?? DateTimeOffset.UtcNow);
                    return Results.Ok(await evaluator.EvaluateEligibleModesAsync(
                        context, request.Facts, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Policy evaluation context or facts are invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost(
            "/operations/v1/policies/evaluate",
            async (
                HardPolicyEvaluationRequest request,
                [FromServices] MonoHardPolicyEvaluationService evaluator,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var context = new EffectivePolicyContext(
                        request.ServiceId, request.CustomerId, request.ContractId,
                        request.BranchId, request.ZoneIds, request.MissionId,
                        request.RuntimeId, request.RequestedAt ?? DateTimeOffset.UtcNow);
                    return Results.Ok(await evaluator.EvaluateAsync(context, request.Facts, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Policy evaluation context or facts are invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }
}

public sealed record HardPolicyEvaluationRequest(
    string? ServiceId,
    string? CustomerId,
    string? ContractId,
    string? BranchId,
    IReadOnlyList<string>? ZoneIds,
    string? MissionId,
    string? RuntimeId,
    DateTimeOffset? RequestedAt,
    JsonElement Facts);
