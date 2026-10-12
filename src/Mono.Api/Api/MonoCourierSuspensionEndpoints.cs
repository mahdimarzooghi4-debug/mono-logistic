using System.Text.Json.Serialization;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierOperationalStatusInput(
    [property: JsonPropertyName("reason_code")] string? ReasonCode);

public static class MonoCourierSuspensionEndpoints
{
    public static IEndpointRouteBuilder MapMonoCourierSuspensionEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/couriers/{courierId}/operational-status", async (
                string courierId,
                PostgresCourierSuspensionService suspension,
                PostgresCourierOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                if (!IsValidCourierId(courierId))
                    return Results.BadRequest(Error("INVALID_REQUEST", "Courier ID is invalid."));
                var snapshot = await suspension.FindAsync(courierId, cancellationToken);
                await readAudit.RecordAsync(
                    PostgresCourierOperationsReadAuditService.CourierDetail,
                    courierId, snapshot is null ? 0 : 1, cancellationToken);
                return snapshot is null
                    ? Results.NotFound(Error("COURIER_NOT_FOUND", "The courier was not found."))
                    : Results.Ok(snapshot);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/couriers/{courierId}/suspend", async (
                string courierId,
                CourierOperationalStatusInput input,
                HttpRequest request,
                PostgresCourierSuspensionService suspension,
                CancellationToken cancellationToken) =>
            {
                if (!IsValidCourierId(courierId) || !TryReadIdempotencyKey(request, out var key))
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "A valid Courier ID and exactly one Idempotency-Key are required."));
                try
                {
                    return MapResult(await suspension.SuspendAsync(
                        courierId, input.ReasonCode ?? string.Empty, key, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST", "Courier suspension fields are invalid."));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/couriers/{courierId}/reinstate", async (
                string courierId,
                CourierOperationalStatusInput input,
                HttpRequest request,
                PostgresCourierSuspensionService suspension,
                CancellationToken cancellationToken) =>
            {
                if (!IsValidCourierId(courierId) || !TryReadIdempotencyKey(request, out var key))
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "A valid Courier ID and exactly one Idempotency-Key are required."));
                try
                {
                    return MapResult(await suspension.ReinstateAsync(
                        courierId, input.ReasonCode ?? string.Empty, key, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST", "Courier reinstatement fields are invalid."));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }

    private static IResult MapResult(CourierOperationalStatusResult result) => result.Outcome switch
    {
        CourierOperationalStatusOutcome.Applied or CourierOperationalStatusOutcome.Replayed
            when result.Snapshot is not null => Results.Ok(result.Snapshot),
        CourierOperationalStatusOutcome.Conflict =>
            Results.Conflict(Error("IDEMPOTENCY_CONFLICT", "The Idempotency-Key was used with a different request.")),
        CourierOperationalStatusOutcome.CourierNotFound =>
            Results.NotFound(Error("COURIER_NOT_FOUND", "The courier was not found.")),
        CourierOperationalStatusOutcome.InvalidState =>
            Results.Conflict(Error("COURIER_STATUS_TRANSITION_NOT_ALLOWED",
                "Only an ACTIVE courier may be suspended, and only a suspended courier may be reinstated.")),
        _ => throw new InvalidOperationException("Unexpected courier status outcome.")
    };

    private static bool TryReadIdempotencyKey(HttpRequest request, out string key)
    {
        key = string.Empty;
        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) ||
            values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            return false;
        key = values[0]!.Trim();
        return key.Length is >= 1 and <= 128 && !key.Any(char.IsControl);
    }

    private static bool IsValidCourierId(string value) =>
        value.Length == 36 && value.StartsWith("crr_", StringComparison.Ordinal) &&
        value.AsSpan(4).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static object Error(string code, string message) => new { error = new { code, message } };
}
