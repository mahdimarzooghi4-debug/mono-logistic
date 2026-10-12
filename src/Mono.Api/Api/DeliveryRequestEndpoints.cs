using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class DeliveryRequestEndpoints
{
    public static IEndpointRouteBuilder MapDeliveryRequestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/delivery-requests", async (
                DeliveryRequestInput input,
                HttpRequest request,
                CustomerBranchRepository branches,
                PostgresDeliveryRequestRepository deliveryRequests,
                CancellationToken cancellationToken) =>
            {
                var idempotencyValues = request.Headers["Idempotency-Key"];
                if (idempotencyValues.Count != 1 ||
                    !DeliveryRequestInputValidator.IsValidIdempotencyKey(idempotencyValues[0]))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "A valid Idempotency-Key header is required.")));
                }

                var errors = DeliveryRequestInputValidator.Validate(input);
                if (errors.Count > 0)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", string.Join(" ", errors))));
                }

                var normalized = input.Normalize();
                var branch = await branches.FindBranchAsync(normalized.BranchId, cancellationToken);
                if (branch is null || !string.Equals(branch.Status, "ACTIVE", StringComparison.Ordinal))
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("BRANCH_NOT_FOUND", "The active branch was not found for this customer.")));
                }

                var result = await deliveryRequests.CreateOrReplayAsync(
                    normalized.BranchId,
                    normalized.ExternalReference,
                    idempotencyValues[0]!,
                    normalized.ToCanonicalJson(),
                    cancellationToken);

                if (result.Outcome == DeliveryRequestWriteOutcome.Conflict)
                {
                    return Results.Conflict(new ApiErrorResponse(
                        new ApiError("IDEMPOTENCY_CONFLICT",
                            "The Idempotency-Key was already used with a different request.")));
                }

                return Results.Ok(new DeliveryRequestResponse(result.DeliveryRequestId, result.Status));
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsWrite);

        endpoints.MapGet("/v1/delivery-requests/{deliveryRequestId}", async (
                string deliveryRequestId,
                PostgresDeliveryRequestRepository deliveryRequests,
                CancellationToken cancellationToken) =>
            {
                var result = await deliveryRequests.FindStatusAsync(deliveryRequestId, cancellationToken);
                if (result is null)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("DELIVERY_REQUEST_NOT_FOUND", "The delivery request was not found for this customer.")));
                }

                return Results.Ok(result);
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsRead);

        return endpoints;
    }
}

public sealed record ApiErrorResponse(ApiError Error);

public sealed record ApiError(string Code, string Message);
