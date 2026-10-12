using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoApiKeyOperationsEndpoints
{
    public static IEndpointRouteBuilder MapMonoApiKeyOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/operations/v1/customers/{customerId}/api-keys",
            async (
                string customerId,
                MonoApiKeyOperationsService apiKeys,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var result = await apiKeys.ListForCustomerAsync(customerId, cancellationToken);
                    return Results.Ok(new MonoApiKeyListResponse(result));
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

        endpoints.MapPost(
            "/operations/v1/customers/{customerId}/api-keys/{keyId}/revoke",
            async (
                string customerId,
                string keyId,
                RevokeMonoApiKeyRequest request,
                MonoApiKeyOperationsService apiKeys,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var result = await apiKeys.RevokeAsync(
                        customerId, keyId, request.Reason ?? string.Empty, cancellationToken);
                    return Results.Ok(result);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Customer ID, API key ID, or reason is invalid.")));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("API_KEY_NOT_FOUND", "API key was not found for this customer.")));
                }
                catch (InvalidOperationException)
                {
                    return Results.Conflict(new ApiErrorResponse(
                        new ApiError("API_KEY_CONFLICT", "API key has already been revoked.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}

public sealed record MonoApiKeyListResponse(IReadOnlyList<ApiKeyInspectionSummary> ApiKeys);

public sealed record RevokeMonoApiKeyRequest(string? Reason);
