using Microsoft.AspNetCore.Mvc;
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


        endpoints.MapGet("/v1/delivery-requests/{deliveryRequestId}/promise", async (
                string deliveryRequestId,
                PostgresPromiseCommitRepository promises,
                CancellationToken cancellationToken) =>
            {
                var result = await promises.FindByDeliveryRequestAsync(deliveryRequestId, cancellationToken);
                if (result is null)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("PROMISE_NOT_FOUND", "The committed promise was not found for this customer.")));
                }

                return Results.Ok(result);
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsRead);

        endpoints.MapGet("/v1/delivery-requests/{deliveryRequestId}/promise/versions", async (
                string deliveryRequestId,
                [FromQuery(Name = "page_size")] int? pageSize,
                [FromQuery(Name = "before_version")] int? beforeVersion,
                PostgresPromiseCommitRepository promises,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 20;
                if (size is < 1 or > 100 || beforeVersion is < 1)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST",
                            "page_size must be between 1 and 100 and before_version must be positive.")));
                }

                var page = await promises.ListVersionsByDeliveryRequestAsync(
                    deliveryRequestId, size, beforeVersion, cancellationToken);
                if (page is null)
                {
                    return Results.NotFound(new ApiErrorResponse(
                        new ApiError("PROMISE_NOT_FOUND", "The committed promise was not found for this customer.")));
                }

                return Results.Ok(page);
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsRead);

        endpoints.MapGet("/v1/delivery-requests", async (
                [FromQuery(Name = "page_size")] int? pageSize,
                string? cursor,
                PostgresDeliveryRequestRepository deliveryRequests,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 50;
                if (size is < 1 or > 100)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "page_size must be between 1 and 100.")));
                }

                if (cursor is not null && !DeliveryRequestInputValidator.IsValidDeliveryRequestId(cursor))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The cursor is invalid for this customer.")));
                }

                var page = await deliveryRequests.ListStatusAsync(size, cursor, cancellationToken);
                if (page is null)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_CURSOR", "The cursor is invalid for this customer.")));
                }

                return Results.Ok(page);
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsRead);

        return endpoints;
    }
}

public sealed record ApiErrorResponse(ApiError Error);

public sealed record ApiError(string Code, string Message);
