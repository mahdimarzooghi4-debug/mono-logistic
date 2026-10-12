using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoMissionLifecycleEndpoints
{
    public static IEndpointRouteBuilder MapMonoMissionLifecycleEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/operations/v1/missions/{missionId}/events", async (
            string missionId,
            MissionLifecycleInput input,
            HttpRequest request,
            PostgresMissionLifecycleRepository lifecycle,
            CancellationToken cancellationToken) =>
        {
            var idempotencyValues = request.Headers["Idempotency-Key"];
            if (idempotencyValues.Count != 1 ||
                !DeliveryRequestInputValidator.IsValidIdempotencyKey(idempotencyValues[0]))
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_REQUEST", "A valid Idempotency-Key header is required.")));
            }

            try
            {
                var result = await lifecycle.ApplyAsync(
                    missionId,
                    input.EventType ?? string.Empty,
                    input.Reason ?? string.Empty,
                    idempotencyValues[0]!,
                    cancellationToken);

                return result.Outcome switch
                {
                    MissionLifecycleOutcome.Applied or MissionLifecycleOutcome.Replayed =>
                        Results.Ok(result.Transition),
                    MissionLifecycleOutcome.Conflict =>
                        Results.Conflict(new ApiErrorResponse(new ApiError(
                            "IDEMPOTENCY_CONFLICT",
                            "The Idempotency-Key was already used for a different Mission event."))),
                    MissionLifecycleOutcome.MissionNotFound =>
                        Results.NotFound(new ApiErrorResponse(new ApiError(
                            "MISSION_NOT_FOUND", "The Mission was not found."))),
                    MissionLifecycleOutcome.InvalidTransition =>
                        Results.Conflict(new ApiErrorResponse(new ApiError(
                            "MISSION_TRANSITION_NOT_ALLOWED",
                            "The requested event is not valid for the Mission's current state."))),
                    _ => throw new ArgumentOutOfRangeException(nameof(result.Outcome))
                };
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(new ApiErrorResponse(new ApiError(
                    "INVALID_REQUEST", "Mission ID, event type, reason, or idempotency key is invalid.")));
            }
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }
}
