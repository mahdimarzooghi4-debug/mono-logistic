using System.Text.Json.Serialization;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record MissionCourierOfferInput(
    [property: JsonPropertyName("courier_id")] string? CourierId,
    [property: JsonPropertyName("zone_id")] string? ZoneId);

public static class MonoMissionCourierOfferEndpoints
{
    public static IEndpointRouteBuilder MapMonoMissionCourierOfferEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/operations/v1/missions/{missionId}/courier-offers", async (
                string missionId,
                MissionCourierOfferInput input,
                HttpRequest request,
                PostgresMissionCourierOfferService offers,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadIdempotencyKey(request, out var key))
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "Exactly one valid Idempotency-Key header is required."));
                try
                {
                    return Map(await offers.CreateOfferAsync(
                        missionId, input.CourierId ?? string.Empty, input.ZoneId ?? string.Empty,
                        key, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "Mission, courier, zone, or idempotency fields are invalid."));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/courier/v1/mission-offers", async (
                IMonoCourierContext courier,
                PostgresMissionCourierOfferService offers,
                CancellationToken cancellationToken) =>
            Results.Ok(await offers.ListMyOffersAsync(courier.CourierId, cancellationToken)))
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapPost("/courier/v1/mission-offers/{offerId}/accept", async (
                string offerId,
                HttpRequest request,
                IMonoCourierContext courier,
                PostgresMissionCourierOfferService offers,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadIdempotencyKey(request, out var key))
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "Exactly one valid Idempotency-Key header is required."));
                try
                {
                    return Map(await offers.AcceptAsync(
                        offerId, courier.CourierId, key, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST", "Offer ID or idempotency key is invalid."));
                }
            })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapPost("/courier/v1/mission-offers/{offerId}/decline", async (
                string offerId,
                HttpRequest request,
                IMonoCourierContext courier,
                PostgresMissionCourierOfferService offers,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadIdempotencyKey(request, out var key))
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "Exactly one valid Idempotency-Key header is required."));
                try
                {
                    return Map(await offers.DeclineAsync(
                        offerId, courier.CourierId, key, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST", "Offer ID or idempotency key is invalid."));
                }
            })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        return endpoints;
    }

    private static IResult Map(MissionCourierOfferResult result) => result.Outcome switch
    {
        MissionCourierOfferOutcome.Applied or MissionCourierOfferOutcome.Replayed
            when result.Offer is not null => Results.Ok(new { offer = result.Offer }),
        MissionCourierOfferOutcome.Conflict =>
            Results.Conflict(Error("IDEMPOTENCY_CONFLICT",
                "The Idempotency-Key was used with a different request.")),
        MissionCourierOfferOutcome.MissionNotFound =>
            Results.NotFound(Error("MISSION_NOT_FOUND", "The mission was not found.")),
        MissionCourierOfferOutcome.CourierNotFound =>
            Results.NotFound(Error("COURIER_NOT_FOUND", "The courier was not found.")),
        MissionCourierOfferOutcome.OfferNotFound =>
            Results.NotFound(Error("MISSION_OFFER_NOT_FOUND", "The mission offer was not found.")),
        MissionCourierOfferOutcome.CourierNotAvailable =>
            Results.Conflict(Error("COURIER_NOT_AVAILABLE",
                "The courier must be active, unsuspended, online, and approved for the selected active zone.")),
        MissionCourierOfferOutcome.InvalidState =>
            Results.Conflict(Error("MISSION_COURIER_OFFER_NOT_ALLOWED",
                "The mission, courier, or offer is not in a state that permits this action.")),
        _ => throw new InvalidOperationException("Unexpected mission courier offer outcome.")
    };

    private static bool TryReadIdempotencyKey(HttpRequest request, out string key)
    {
        key = string.Empty;
        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) ||
            values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            return false;
        key = values[0]!.Trim();
        return DeliveryRequestInputValidator.IsValidIdempotencyKey(key);
    }

    private static object Error(string code, string message) =>
        new { error = new { code, message } };
}
