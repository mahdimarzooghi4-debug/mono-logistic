using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class CustomerServiceConfigurationAuthoringEndpoints
{
    public static IEndpointRouteBuilder MapCustomerServiceConfigurationAuthoringEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        const string route =
            "/operations/v1/customers/{customerId}/services/{serviceCode}/configurations";

        endpoints.MapPost(route, async (
                string customerId,
                string serviceCode,
                CreateCustomerServiceConfigurationDraftRequest request,
                CustomerServiceConfigurationAuthoringService authoring,
                CancellationToken cancellationToken) =>
            {
                if (request.EffectiveFrom == default)
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "EffectiveFrom is required.")));

                try
                {
                    var draft = await authoring.CreateDraftAsync(
                        customerId,
                        serviceCode,
                        request.EffectiveFrom,
                        request.EffectiveTo,
                        request.Configuration,
                        request.Reason,
                        cancellationToken);
                    return Results.Created(route, CustomerServiceConfigurationDraftSummary.From(draft));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST",
                            "Customer, service, effective dates, configuration, or reason is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost(route + "/{serviceConfigurationId}/activate", async (
                string customerId,
                string serviceCode,
                string serviceConfigurationId,
                ActivateCustomerServiceConfigurationDraftRequest request,
                CustomerServiceConfigurationAuthoringService authoring,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    await authoring.ActivateDraftAsync(
                        customerId, serviceConfigurationId, request.Reason, cancellationToken);
                    return Results.NoContent();
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer, configuration, or reason is invalid.")));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("CONFIGURATION_NOT_FOUND",
                            "The draft configuration was not found for this customer.")));
                }
                catch (InvalidOperationException)
                {
                    return Results.Conflict(new ApiErrorResponse(
                        new ApiError("CONFIGURATION_CONFLICT",
                            "The draft cannot be activated in its current state or effective period.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}

public sealed record CreateCustomerServiceConfigurationDraftRequest(
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    JsonElement Configuration,
    string Reason);

public sealed record ActivateCustomerServiceConfigurationDraftRequest(string Reason);

public sealed record CustomerServiceConfigurationDraftSummary(
    string ServiceConfigurationId,
    string CustomerId,
    string ServiceCode,
    int Version,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    DateTimeOffset CreatedAt)
{
    public static CustomerServiceConfigurationDraftSummary From(CustomerServiceConfiguration draft) =>
        new(draft.ServiceConfigurationId, draft.CustomerId, draft.ServiceCode, draft.Version,
            draft.Status, draft.EffectiveFrom, draft.EffectiveTo, draft.CreatedAt);
}
