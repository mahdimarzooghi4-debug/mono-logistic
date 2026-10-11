using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MissionConfirmationEndpoints
{
    public static IEndpointRouteBuilder MapMissionConfirmationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/delivery-requests/{deliveryRequestId}/confirm", async (
                string deliveryRequestId,
                MissionConfirmationInput input,
                HttpRequest request,
                PostgresMissionConfirmationRepository confirmations,
                CancellationToken cancellationToken) =>
            {
                var idempotencyValues = request.Headers["Idempotency-Key"];
                if (idempotencyValues.Count != 1 ||
                    !DeliveryRequestInputValidator.IsValidIdempotencyKey(idempotencyValues[0]))
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "A valid Idempotency-Key header is required.")));
                }

                if (!DeliveryRequestInputValidator.IsValidDeliveryRequestId(deliveryRequestId) ||
                    string.IsNullOrWhiteSpace(input.QuoteId) ||
                    input.QuoteId.Length > 128 ||
                    input.PromiseVersion < 1)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST",
                            "A valid delivery request ID, quote_id, and positive promise_version are required.")));
                }

                var result = await confirmations.ConfirmAsync(
                    deliveryRequestId,
                    input.QuoteId,
                    input.PromiseVersion,
                    idempotencyValues[0]!,
                    cancellationToken);

                return result.Outcome switch
                {
                    MissionConfirmationOutcome.Created or MissionConfirmationOutcome.Replayed =>
                        Results.Ok(result.Mission),
                    MissionConfirmationOutcome.Conflict =>
                        Results.Conflict(new ApiErrorResponse(
                            new ApiError("IDEMPOTENCY_CONFLICT",
                                "The Idempotency-Key was already used with a different confirmation."))),
                    MissionConfirmationOutcome.AlreadyConfirmed =>
                        Results.Conflict(new ApiErrorResponse(
                            new ApiError("MISSION_ALREADY_CONFIRMED",
                                "A Mission has already been confirmed for this delivery request."))),
                    MissionConfirmationOutcome.DeliveryRequestNotFound =>
                        Results.NotFound(new ApiErrorResponse(
                            new ApiError("DELIVERY_REQUEST_NOT_FOUND",
                                "The delivery request was not found for this customer."))),
                    MissionConfirmationOutcome.QuoteNotFound =>
                        Results.NotFound(new ApiErrorResponse(
                            new ApiError("QUOTE_NOT_FOUND",
                                "The requested Quote version was not found for this delivery request."))),
                    MissionConfirmationOutcome.PromiseNotFound =>
                        Results.NotFound(new ApiErrorResponse(
                            new ApiError("PROMISE_NOT_FOUND",
                                "The requested current Promise version was not found for this delivery request."))),
                    _ => throw new ArgumentOutOfRangeException(nameof(result.Outcome))
                };
            })
            .RequireAuthorization(MonoAuthorizationPolicies.DeliveryRequestsWrite);

        return endpoints;
    }
}
