using System.Text.Json.Serialization;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierLivePresenceInput(
    [property: JsonPropertyName("zone_id")] string? ZoneId);

public sealed record CourierLivePresenceResponse(
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("zone_id")] string? ZoneId,
    [property: JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt)
{
    public static CourierLivePresenceResponse From(CourierLivePresenceSnapshot snapshot) =>
        new(snapshot.CourierId, snapshot.Status, snapshot.ZoneId, snapshot.UpdatedAt);
}

public static class MonoCourierLivePresenceEndpoints
{
    public static IEndpointRouteBuilder MapMonoCourierLivePresenceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/courier/v1/presence", async (
                IMonoCourierContext courier,
                PostgresCourierLivePresenceService presence,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(CourierLivePresenceResponse.From(
                        await presence.FindAsync(courier.CourierId, cancellationToken)));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound(Error("COURIER_NOT_FOUND", "The courier was not found."));
                }
            })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapPost("/courier/v1/presence/online", async (
                CourierLivePresenceInput input,
                HttpRequest request,
                IMonoCourierContext courier,
                PostgresCourierLivePresenceService presence,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadIdempotencyKey(request, out var key) || string.IsNullOrWhiteSpace(input.ZoneId))
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "A valid Idempotency-Key and zone_id are required."));
                try
                {
                    return Map(await presence.GoOnlineAsync(
                        courier.CourierId, input.ZoneId, key, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST", "Courier presence fields are invalid."));
                }
            })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapPost("/courier/v1/presence/offline", async (
                HttpRequest request,
                IMonoCourierContext courier,
                PostgresCourierLivePresenceService presence,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadIdempotencyKey(request, out var key))
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "Exactly one valid Idempotency-Key header is required."));
                return Map(await presence.GoOfflineAsync(courier.CourierId, key, cancellationToken));
            })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapGet("/operations/v1/couriers/{courierId}/presence", async (
                string courierId,
                PostgresCourierLivePresenceService presence,
                PostgresCourierOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(courierId, @"\Acrr_[0-9a-f]{32}\z"))
                    return Results.BadRequest(Error("INVALID_REQUEST", "Courier ID is invalid."));
                CourierLivePresenceSnapshot? snapshot = null;
                try
                {
                    snapshot = await presence.FindAsync(courierId, cancellationToken);
                }
                catch (KeyNotFoundException)
                {
                    // Record the attempted detail read below even when it misses.
                }
                await readAudit.RecordAsync(
                    PostgresCourierOperationsReadAuditService.CourierDetail,
                    courierId, snapshot is null ? 0 : 1, cancellationToken);
                return snapshot is null
                    ? Results.NotFound(Error("COURIER_NOT_FOUND", "The courier was not found."))
                    : Results.Ok(CourierLivePresenceResponse.From(snapshot));
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }

    private static IResult Map(CourierLivePresenceResult result) => result.Outcome switch
    {
        CourierLivePresenceOutcome.Applied or CourierLivePresenceOutcome.Replayed
            when result.Snapshot is not null =>
                Results.Ok(CourierLivePresenceResponse.From(result.Snapshot)),
        CourierLivePresenceOutcome.Conflict =>
            Results.Conflict(Error("IDEMPOTENCY_CONFLICT",
                "The Idempotency-Key was used with a different request.")),
        CourierLivePresenceOutcome.CourierNotFound =>
            Results.NotFound(Error("COURIER_NOT_FOUND", "The courier was not found.")),
        CourierLivePresenceOutcome.InvalidZone =>
            Results.Conflict(Error("COURIER_ZONE_NOT_APPROVED",
                "The courier's selected zone is not currently approved and active.")),
        CourierLivePresenceOutcome.InvalidState =>
            Results.Conflict(Error("COURIER_PRESENCE_TRANSITION_NOT_ALLOWED",
                "The courier must be active and unsuspended to change presence; online and offline transitions must alternate.")),
        _ => throw new InvalidOperationException("Unexpected courier live presence outcome.")
    };

    private static bool TryReadIdempotencyKey(HttpRequest request, out string key)
    {
        key = string.Empty;
        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) ||
            values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            return false;
        key = values[0]!.Trim();
        return System.Text.RegularExpressions.Regex.IsMatch(key, @"\A[A-Za-z0-9._~-]{1,128}\z");
    }

    private static object Error(string code, string message) =>
        new { error = new { code, message } };
}
