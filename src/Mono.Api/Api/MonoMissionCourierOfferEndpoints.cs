using System.Text.Json.Serialization;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record MissionCourierOfferInput(
    [property: JsonPropertyName("courier_id")] string? CourierId,
    [property: JsonPropertyName("zone_id")] string? ZoneId);

public sealed record MissionCourierOfferBatchCommandInput(
    [property: JsonPropertyName("mission_id")] string? MissionId,
    [property: JsonPropertyName("courier_id")] string? CourierId,
    [property: JsonPropertyName("zone_id")] string? ZoneId,
    [property: JsonPropertyName("idempotency_key")] string? IdempotencyKey);

public sealed record MissionCourierOfferBatchInput(
    [property: JsonPropertyName("commands")] IReadOnlyList<MissionCourierOfferBatchCommandInput?>? Commands);

public sealed record MissionCourierOfferExpiryBatchCommandInput(
    [property: JsonPropertyName("mission_id")] string? MissionId,
    [property: JsonPropertyName("offer_id")] string? OfferId,
    [property: JsonPropertyName("idempotency_key")] string? IdempotencyKey);

public sealed record MissionCourierOfferExpiryBatchInput(
    [property: JsonPropertyName("commands")] IReadOnlyList<MissionCourierOfferExpiryBatchCommandInput?>? Commands);

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

        endpoints.MapPost("/operations/v1/dispatch/offers/batch", async (
                MissionCourierOfferBatchInput input,
                PostgresMissionCourierOfferService offers,
                CancellationToken cancellationToken) =>
            {
                var submitted = input.Commands;
                if (submitted is null ||
                    submitted.Count is < 1 or > PostgresMissionCourierOfferService.MaximumDispatchOfferBatchSize ||
                    submitted.Any(command => command is null ||
                        string.IsNullOrWhiteSpace(command.MissionId) ||
                        string.IsNullOrWhiteSpace(command.CourierId) ||
                        string.IsNullOrWhiteSpace(command.ZoneId) ||
                        string.IsNullOrWhiteSpace(command.IdempotencyKey)))
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        $"Provide between 1 and {PostgresMissionCourierOfferService.MaximumDispatchOfferBatchSize} complete offer commands."));

                var commands = submitted.Select(command => new MissionCourierOfferBatchCommand(
                    command!.MissionId!, command.CourierId!, command.ZoneId!, command.IdempotencyKey!)).ToArray();
                try
                {
                    var items = await offers.CreateBatchAsync(commands, cancellationToken);
                    return Results.Ok(new { items });
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "Each command must contain valid IDs and keys, with no duplicate Missions."));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/missions/{missionId}/courier-offers", async (
                string missionId,
                PostgresMissionCourierOfferService offers,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    return Results.Ok(await offers.ListForMissionAsync(missionId, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST", "Mission ID is invalid."));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/missions/{missionId}/courier-offers/{offerId}/events", async (
                string missionId,
                string offerId,
                PostgresMissionCourierOfferService offers,
                PostgresMissionOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var history = await offers.ListOfferEventsAsync(missionId, offerId, cancellationToken);
                    if (history is null)
                        return Results.NotFound(Error("MISSION_OFFER_NOT_FOUND", "The mission offer was not found."));
                    await readAudit.RecordAsync(PostgresMissionOperationsReadAuditService.MissionCourierOfferHistory,
                        missionId, history.Count, cancellationToken);
                    return Results.Ok(new { events = history });
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST", "Mission or offer ID is invalid."));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/dispatch/queue/expire-overdue", async (
                MissionCourierOfferExpiryBatchInput input,
                PostgresMissionCourierOfferService offers,
                CancellationToken cancellationToken) =>
            {
                var submitted = input.Commands;
                if (submitted is null || submitted.Count is < 1 or > PostgresMissionCourierOfferService.MaximumExpiryBatchSize ||
                    submitted.Any(command => command is null ||
                        string.IsNullOrWhiteSpace(command.MissionId) ||
                        string.IsNullOrWhiteSpace(command.OfferId) ||
                        string.IsNullOrWhiteSpace(command.IdempotencyKey)))
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        $"Provide between 1 and {PostgresMissionCourierOfferService.MaximumExpiryBatchSize} complete expiry commands."));

                var commands = submitted.Select(command => new MissionCourierOfferExpiryCommand(
                    command!.MissionId!, command.OfferId!, command.IdempotencyKey!)).ToArray();
                try
                {
                    var items = await offers.ExpireBatchAsync(commands, cancellationToken);
                    return Results.Ok(new { items });
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "Each command must contain valid mission and offer IDs, unique offers, and valid idempotency keys."));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/missions/{missionId}/courier-offers/{offerId}/expire", async (
                string missionId,
                string offerId,
                HttpRequest request,
                PostgresMissionCourierOfferService offers,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadIdempotencyKey(request, out var key))
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "Exactly one valid Idempotency-Key header is required."));
                try
                {
                    return Map(await offers.ExpireAsync(missionId, offerId, key, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST", "Mission or offer ID is invalid."));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/missions/{missionId}/courier-offers/{offerId}/release-assignment", async (
                string missionId,
                string offerId,
                HttpRequest request,
                PostgresMissionCourierOfferService offers,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadIdempotencyKey(request, out var key))
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "Exactly one valid Idempotency-Key header is required."));
                try
                {
                    return Map(await offers.ReleaseAssignmentAsync(missionId, offerId, key, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST", "Mission or offer ID is invalid."));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/missions/{missionId}/courier-offers/{offerId}/withdraw", async (
                string missionId,
                string offerId,
                HttpRequest request,
                PostgresMissionCourierOfferService offers,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadIdempotencyKey(request, out var key))
                    return Results.BadRequest(Error("INVALID_REQUEST",
                        "Exactly one valid Idempotency-Key header is required."));
                try
                {
                    return Map(await offers.WithdrawAsync(missionId, offerId, key, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST", "Mission or offer ID is invalid."));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/courier/v1/mission-offers", async (
                IMonoCourierContext courier,
                PostgresMissionCourierOfferService offers,
                CancellationToken cancellationToken) =>
            Results.Ok(await offers.ListMyOffersAsync(courier.CourierId, cancellationToken)))
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapGet("/courier/v1/missions/active", async (
                HttpResponse response, IMonoCourierContext courier,
                PostgresCourierMissionOfferRouteContextService routeContext, CancellationToken cancellationToken) =>
            {
                response.Headers.CacheControl = "no-store";
                try { return Results.Ok(new { mission = await routeContext.FindActiveAsync(courier.CourierId, cancellationToken) }); }
                catch (ArgumentException) { return Results.BadRequest(Error("INVALID_REQUEST", "Courier identity is invalid.")); }
            })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapGet("/courier/v1/mission-offers/{offerId}/route-context", async (
                string offerId,
                HttpResponse response,
                IMonoCourierContext courier,
                PostgresCourierMissionOfferRouteContextService routeContext,
                CancellationToken cancellationToken) =>
            {
                response.Headers.CacheControl = "no-store";
                try
                {
                    var context = await routeContext.FindAsync(courier.CourierId, offerId, cancellationToken);
                    return context is null
                        ? Results.NotFound(Error("MISSION_OFFER_NOT_FOUND", "The active mission offer was not found."))
                        : Results.Ok(context);
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(Error("INVALID_REQUEST", "Offer ID is invalid."));
                }
            })
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
