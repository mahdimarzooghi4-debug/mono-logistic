using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierRegistrationInput(
    [property: JsonPropertyName("mode")] string? Mode,
    [property: JsonPropertyName("preferred_zone_ids")] IReadOnlyCollection<string>? PreferredZoneIds);

public sealed record CourierTrustTransitionInput(
    [property: JsonPropertyName("target_status")] string? TargetStatus,
    [property: JsonPropertyName("approved_zone_ids")] IReadOnlyCollection<string>? ApprovedZoneIds,
    [property: JsonPropertyName("primary_zone_id")] string? PrimaryZoneId,
    [property: JsonPropertyName("reason_code")] string? ReasonCode,
    [property: JsonPropertyName("evidence_reference")] string? EvidenceReference);

public sealed record CourierAvailabilityWindowInput(
    [property: JsonPropertyName("zone_id")] string? ZoneId,
    [property: JsonPropertyName("starts_at")] DateTimeOffset StartsAt,
    [property: JsonPropertyName("ends_at")] DateTimeOffset EndsAt);

public sealed record CourierAvailabilityCancellationInput(
    [property: JsonPropertyName("reason")] string? Reason);

public static class MonoCourierOperationsEndpoints
{
    public static IEndpointRouteBuilder MapMonoCourierOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/operations/v1/couriers", async (
                CourierRegistrationInput input,
                HttpRequest request,
                PostgresCourierOnboardingService couriers,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadIdempotencyKey(request, out var key))
                    return InvalidRequest("Exactly one valid Idempotency-Key header is required.");

                try
                {
                    var result = await couriers.RegisterAsync(
                        input.Mode ?? string.Empty,
                        input.PreferredZoneIds ?? [],
                        key,
                        cancellationToken);
                    return result.Outcome switch
                    {
                        CourierOperationsWriteOutcome.Applied when result.Snapshot is not null =>
                            Results.Created($"/operations/v1/couriers/{result.Snapshot.CourierId}", result.Snapshot),
                        CourierOperationsWriteOutcome.Replayed when result.Snapshot is not null =>
                            Results.Ok(result.Snapshot),
                        CourierOperationsWriteOutcome.Conflict =>
                            Results.Conflict(Error("IDEMPOTENCY_CONFLICT",
                                "The Idempotency-Key was used with a different request.")),
                        CourierOperationsWriteOutcome.InvalidZone =>
                            Results.UnprocessableEntity(Error("ZONE_NOT_ACTIVE",
                                "Every preferred zone must have an active Mono boundary.")),
                        _ => throw new InvalidOperationException("Unexpected courier registration outcome.")
                    };
                }
                catch (ArgumentException)
                {
                    return InvalidRequest("Courier registration fields are invalid.");
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/couriers", async (
                [FromQuery(Name = "page_size")] int? pageSize,
                string? cursor,
                string? query,
                string? status,
                PostgresCourierOnboardingService couriers,
                PostgresCourierOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 50;
                if (size is < 1 or > 100)
                    return InvalidRequest("Courier page_size must be between 1 and 100.");

                try
                {
                    var page = await couriers.ListAsync(size, cursor, cancellationToken, query, status);
                    if (page is null)
                        return Results.BadRequest(Error("INVALID_CURSOR", "The courier cursor is not valid."));
                    await readAudit.RecordAsync(
                        PostgresCourierOperationsReadAuditService.CourierSearch,
                        null, page.Items.Count, cancellationToken);
                    return Results.Ok(page);
                }
                catch (ArgumentException)
                {
                    return InvalidRequest("Courier list filters are invalid.");
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/couriers/{courierId}", async (
                string courierId,
                PostgresCourierOnboardingService couriers,
                PostgresCourierOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                if (!IsValidCourierId(courierId))
                    return InvalidRequest("Courier ID is invalid.");
                var courier = await couriers.FindAsync(courierId, cancellationToken);
                await readAudit.RecordAsync(
                    PostgresCourierOperationsReadAuditService.CourierDetail,
                    courierId, courier is null ? 0 : 1, cancellationToken);
                return courier is null
                    ? Results.NotFound(Error("COURIER_NOT_FOUND", "The courier was not found."))
                    : Results.Ok(courier);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/courier-read-audit", async (
                [FromQuery(Name = "page_size")] int? pageSize,
                string? cursor,
                PostgresCourierOperationsReadAuditService readAudit,
                CancellationToken cancellationToken) =>
            {
                var size = pageSize ?? 50;
                if (size is < 1 or > 100)
                    return InvalidRequest("Courier audit page_size must be between 1 and 100.");
                try
                {
                    var page = await readAudit.ListAsync(size, cursor, cancellationToken);
                    return page is null
                        ? Results.BadRequest(Error("INVALID_CURSOR", "The courier audit cursor is not valid."))
                        : Results.Ok(page);
                }
                catch (ArgumentException)
                {
                    return InvalidRequest("Courier audit filters are invalid.");
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/couriers/{courierId}/trust-transitions", async (
                string courierId,
                CourierTrustTransitionInput input,
                HttpRequest request,
                PostgresCourierOnboardingService couriers,
                CancellationToken cancellationToken) =>
            {
                if (!IsValidCourierId(courierId) || !TryReadIdempotencyKey(request, out var key))
                    return InvalidRequest("A valid Courier ID and exactly one Idempotency-Key are required.");

                try
                {
                    var result = await couriers.AdvanceTrustAsync(
                        courierId,
                        input.TargetStatus ?? string.Empty,
                        input.ApprovedZoneIds,
                        input.PrimaryZoneId,
                        input.ReasonCode ?? string.Empty,
                        input.EvidenceReference ?? string.Empty,
                        key,
                        cancellationToken);
                    return result.Outcome switch
                    {
                        CourierOperationsWriteOutcome.Applied or CourierOperationsWriteOutcome.Replayed
                            when result.Snapshot is not null => Results.Ok(result.Snapshot),
                        CourierOperationsWriteOutcome.Conflict =>
                            Results.Conflict(Error("IDEMPOTENCY_CONFLICT",
                                "The Idempotency-Key was used with a different request.")),
                        CourierOperationsWriteOutcome.CourierNotFound =>
                            Results.NotFound(Error("COURIER_NOT_FOUND", "The courier was not found.")),
                        CourierOperationsWriteOutcome.InvalidTransition =>
                            Results.Conflict(Error("COURIER_TRUST_TRANSITION_NOT_ALLOWED",
                                "The courier trust transition or approval data is not valid for its current state.")),
                        CourierOperationsWriteOutcome.InvalidZone =>
                            Results.UnprocessableEntity(Error("ZONE_NOT_ACTIVE",
                                "Every approved zone must have an active Mono boundary.")),
                        _ => throw new InvalidOperationException("Unexpected courier trust outcome.")
                    };
                }
                catch (ArgumentException)
                {
                    return InvalidRequest("Courier trust transition fields are invalid.");
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/courier-capacity-projection", async (
                [FromQuery(Name = "zone_id")] string? zoneId,
                [FromQuery(Name = "from")] DateTimeOffset? from,
                [FromQuery(Name = "to")] DateTimeOffset? to,
                [FromQuery(Name = "bucket_minutes")] int? bucketMinutes,
                PostgresCourierCapacityProjectionService capacity,
                CancellationToken cancellationToken) =>
            {
                if (zoneId is null || from is null || to is null || bucketMinutes is null)
                    return InvalidRequest("Capacity projection requires zone_id, from, to, and bucket_minutes.");
                try
                {
                    return Results.Ok(await capacity.ProjectAsync(
                        zoneId, from.Value, to.Value, bucketMinutes.Value, cancellationToken));
                }
                catch (ArgumentException)
                {
                    return InvalidRequest("Capacity projection filters are invalid.");
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/couriers/{courierId}/availability-windows", async (
                string courierId,
                CourierAvailabilityWindowInput input,
                HttpRequest request,
                PostgresCourierAvailabilityService availability,
                CancellationToken cancellationToken) =>
            {
                if (!IsValidCourierId(courierId) || !TryReadIdempotencyKey(request, out var key))
                    return InvalidRequest("A valid Courier ID and exactly one Idempotency-Key are required.");
                try
                {
                    var result = await availability.CreateAsync(
                        courierId, input.ZoneId ?? string.Empty, input.StartsAt, input.EndsAt, key, cancellationToken);
                    return result.Outcome switch
                    {
                        CourierAvailabilityWriteOutcome.Applied when result.Window is not null =>
                            Results.Created($"/operations/v1/courier-availability-windows/{result.Window.WindowId}", result.Window),
                        CourierAvailabilityWriteOutcome.Replayed when result.Window is not null =>
                            Results.Ok(result.Window),
                        CourierAvailabilityWriteOutcome.Conflict =>
                            Results.Conflict(Error("IDEMPOTENCY_CONFLICT",
                                "The Idempotency-Key was used with a different request.")),
                        CourierAvailabilityWriteOutcome.CourierNotFound =>
                            Results.NotFound(Error("COURIER_NOT_FOUND", "The courier was not found.")),
                        CourierAvailabilityWriteOutcome.InvalidCourierState =>
                            Results.Conflict(Error("COURIER_NOT_ACTIVE", "Availability requires an ACTIVE courier.")),
                        CourierAvailabilityWriteOutcome.InvalidZone =>
                            Results.UnprocessableEntity(Error("ZONE_NOT_APPROVED",
                                "The zone must be active and approved for this courier.")),
                        CourierAvailabilityWriteOutcome.Overlap =>
                            Results.Conflict(Error("AVAILABILITY_WINDOW_OVERLAP",
                                "The courier already has a scheduled window in this period.")),
                        _ => throw new InvalidOperationException("Unexpected courier availability outcome.")
                    };
                }
                catch (ArgumentException)
                {
                    return InvalidRequest("Courier availability window fields are invalid.");
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/courier-availability-windows", async (
                [FromQuery(Name = "from")] DateTimeOffset? from,
                [FromQuery(Name = "to")] DateTimeOffset? to,
                [FromQuery(Name = "page_size")] int? pageSize,
                string? zone_id,
                string? cursor,
                PostgresCourierAvailabilityService availability,
                CancellationToken cancellationToken) =>
            {
                if (from is null || to is null)
                    return InvalidRequest("Availability search requires from and to timestamps.");
                var size = pageSize ?? 50;
                if (size is < 1 or > 100)
                    return InvalidRequest("Availability page_size must be between 1 and 100.");
                try
                {
                    var page = await availability.ListAsync(
                        from.Value, to.Value, size, zone_id, cursor, cancellationToken);
                    return page is null
                        ? Results.BadRequest(Error("INVALID_CURSOR", "The availability cursor is not valid."))
                        : Results.Ok(page);
                }
                catch (ArgumentException)
                {
                    return InvalidRequest("Availability search filters are invalid.");
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/courier-availability-windows/{windowId}/cancel", async (
                string windowId,
                CourierAvailabilityCancellationInput input,
                HttpRequest request,
                PostgresCourierAvailabilityService availability,
                CancellationToken cancellationToken) =>
            {
                if (!TryReadIdempotencyKey(request, out var key))
                    return InvalidRequest("Exactly one valid Idempotency-Key header is required.");
                try
                {
                    var result = await availability.CancelAsync(
                        windowId, input.Reason ?? string.Empty, key, cancellationToken);
                    return result.Outcome switch
                    {
                        CourierAvailabilityWriteOutcome.Applied or CourierAvailabilityWriteOutcome.Replayed
                            when result.Window is not null => Results.Ok(result.Window),
                        CourierAvailabilityWriteOutcome.Conflict =>
                            Results.Conflict(Error("IDEMPOTENCY_CONFLICT",
                                "The Idempotency-Key was used with a different request.")),
                        CourierAvailabilityWriteOutcome.WindowNotFound =>
                            Results.NotFound(Error("AVAILABILITY_WINDOW_NOT_FOUND",
                                "The scheduled courier window was not found.")),
                        _ => throw new InvalidOperationException("Unexpected courier availability cancellation outcome.")
                    };
                }
                catch (ArgumentException)
                {
                    return InvalidRequest("Courier availability cancellation fields are invalid.");
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/courier-planning-eligibility", async (
                [FromQuery(Name = "zone_id")] string? zoneId,
                [FromQuery(Name = "from")] DateTimeOffset? from,
                [FromQuery(Name = "to")] DateTimeOffset? to,
                [FromQuery(Name = "page_size")] int? pageSize,
                string? cursor,
                PostgresCourierPlanningEligibilityService eligibility,
                CancellationToken cancellationToken) =>
            {
                if (zoneId is null || from is null || to is null)
                    return InvalidRequest("Planning eligibility requires zone_id, from, and to timestamps.");
                var size = pageSize ?? 50;
                if (size is < 1 or > 100)
                    return InvalidRequest("Planning eligibility page_size must be between 1 and 100.");
                try
                {
                    var page = await eligibility.SearchAsync(
                        zoneId, from.Value, to.Value, size, cursor, cancellationToken);
                    return page is null
                        ? Results.BadRequest(Error("INVALID_CURSOR", "The courier cursor is not valid for this search."))
                        : Results.Ok(page);
                }
                catch (ArgumentException)
                {
                    return InvalidRequest("Planning eligibility filters are invalid.");
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }

    private static bool TryReadIdempotencyKey(HttpRequest request, out string key)
    {
        var values = request.Headers["Idempotency-Key"];
        key = values.Count == 1 ? values[0]?.Trim() ?? string.Empty : string.Empty;
        return key.Length is > 0 and <= 128 && !key.Any(char.IsControl);
    }

    private static bool IsValidCourierId(string value) =>
        value.Length == 36 && value.StartsWith("crr_", StringComparison.Ordinal) &&
        value[4..].All(character =>
            (character is >= '0' and <= '9') || (character is >= 'a' and <= 'f'));

    private static IResult InvalidRequest(string message) =>
        Results.BadRequest(Error("INVALID_REQUEST", message));

    private static ApiErrorResponse Error(string code, string message) =>
        new(new ApiError(code, message));
}
