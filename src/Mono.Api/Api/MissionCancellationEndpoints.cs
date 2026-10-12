using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MissionCancellationEndpoints
{
    private static readonly Regex MissionIdPattern = new(
        @"\Amis_[0-9a-f]{32}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ExternalReferencePattern = new(
        @"\A[A-Za-z0-9._~-]{1,128}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly IReadOnlySet<string> PostHandoffStatuses = new HashSet<string>(
        ["HANDED_TO_COURIER", "PICKED_UP", "IN_TRANSIT", "ARRIVED_DROPOFF", "DELIVERED"],
        StringComparer.Ordinal);

    public static IEndpointRouteBuilder MapMissionCancellationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/missions/{missionId}/cancel", async (
                string missionId,
                MissionCancellationInput input,
                HttpRequest request,
                PostgresMissionLifecycleRepository lifecycle,
                CancellationToken cancellationToken) =>
            {
                var idempotencyValues = request.Headers["Idempotency-Key"];
                if (!MissionIdPattern.IsMatch(missionId) ||
                    idempotencyValues.Count != 1 ||
                    !DeliveryRequestInputValidator.IsValidIdempotencyKey(idempotencyValues[0]) ||
                    !string.Equals(input.ReasonCode, "CUSTOMER_CANCELLED", StringComparison.Ordinal) ||
                    (input.ExternalReference is not null &&
                     !ExternalReferencePattern.IsMatch(input.ExternalReference)))
                {
                    return Results.BadRequest(new ApiErrorResponse(new ApiError(
                        "INVALID_REQUEST",
                        "A valid Mission ID, Idempotency-Key, CUSTOMER_CANCELLED reason_code, and optional external_reference are required.")));
                }

                var result = await lifecycle.CancelForCustomerAsync(
                    missionId,
                    input.ReasonCode!,
                    input.ExternalReference,
                    idempotencyValues[0]!,
                    cancellationToken);

                return result.Outcome switch
                {
                    MissionLifecycleOutcome.Applied or MissionLifecycleOutcome.Replayed =>
                        Results.Ok(new MissionCancellationResponse(
                            result.Transition!.MissionId,
                            result.Transition.Status)),
                    MissionLifecycleOutcome.Conflict =>
                        Results.Conflict(new ApiErrorResponse(new ApiError(
                            "IDEMPOTENCY_CONFLICT",
                            "The Idempotency-Key was already used with a different cancellation request."))),
                    MissionLifecycleOutcome.MissionNotFound =>
                        Results.NotFound(new ApiErrorResponse(new ApiError(
                            "MISSION_NOT_FOUND", "The Mission was not found for this customer."))),
                    MissionLifecycleOutcome.InvalidTransition =>
                        CancellationNotAllowed(result.CurrentStatus),
                    _ => throw new ArgumentOutOfRangeException(nameof(result.Outcome))
                };
            })
            .RequireAuthorization(MonoAuthorizationPolicies.MissionsCancel);

        return endpoints;
    }

    private static IResult CancellationNotAllowed(string? currentStatus)
    {
        var reasonCode = currentStatus is not null && PostHandoffStatuses.Contains(currentStatus)
            ? "HANDOFF_ALREADY_CONFIRMED"
            : "MISSION_NOT_ACTIVE";

        return Results.Conflict(new
        {
            error = new
            {
                code = "CANCELLATION_NOT_ALLOWED",
                current_status = currentStatus,
                reason_code = reasonCode
            }
        });
    }
}
