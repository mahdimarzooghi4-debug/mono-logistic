using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierPayoutDestinationAuditItemResponse(
    [property: JsonPropertyName("event_key")] string EventKey,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("evidence_reference")] string? EvidenceReference,
    [property: JsonPropertyName("outcome_code")] string? OutcomeCode,
    [property: JsonPropertyName("masked_iban")] string? MaskedIban);

public sealed record CourierPayoutDestinationAuditPageResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<CourierPayoutDestinationAuditItemResponse> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public static class MonoCourierPayoutDestinationAuditEndpoints
{
    public static IEndpointRouteBuilder MapMonoCourierPayoutDestinationAuditEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/couriers/{courierId}/payout-destination/audit",
                async Task<IResult> (
                    string courierId,
                    HttpContext httpContext,
                    PostgresCourierPayoutDestinationAuditReadService service,
                    TimeProvider timeProvider,
                    int? limit,
                    string? cursor,
                    CancellationToken cancellationToken) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var pageSize = limit ?? 50;
                    if (pageSize is < 1 or > 100)
                        return Results.BadRequest(new { error_code = "INVALID_LIMIT" });
                    if (!IsCourierId(courierId))
                        return Results.BadRequest(new { error_code = "INVALID_COURIER_ID" });
                    if (!TryDecodeCursor(cursor, courierId, out var decodedCursor))
                        return Results.BadRequest(new { error_code = "INVALID_CURSOR" });

                    var page = await service.ReadAsync(courierId, pageSize, decodedCursor,
                        timeProvider.GetUtcNow(), cancellationToken);
                    if (page.OutcomeCode == "NOT_FOUND")
                        return Results.NotFound(new { error_code = "COURIER_NOT_FOUND" });
                    var next = page.NextCursor is null ? null : EncodeCursor(courierId, page.NextCursor);
                    return Results.Ok(new CourierPayoutDestinationAuditPageResponse(
                        page.Items.Select(item => new CourierPayoutDestinationAuditItemResponse(
                            item.EventKey, item.EventType, item.Action, item.ActorId, item.OccurredAt,
                            item.EvidenceReference, item.OutcomeCode, item.MaskedIban)).ToArray(),
                        next));
                })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        endpoints.MapGet("/operations/v1/payout-destinations/audit-search",
                async Task<IResult> (
                    HttpContext httpContext, PostgresCourierPayoutDestinationAuditSearchService service,
                    TimeProvider timeProvider, int? limit, string? courier_id, string? event_type,
                    string? action, string? actor_id, DateTimeOffset? from_at, DateTimeOffset? to_at,
                    string? cursor, CancellationToken cancellationToken) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var pageSize = limit ?? 50;
                    if (pageSize is < 1 or > 100)
                        return Results.BadRequest(new { error_code = "INVALID_LIMIT" });
                    var courierId = string.IsNullOrWhiteSpace(courier_id) ? null : courier_id.Trim();
                    if (courierId is not null && !IsCourierId(courierId))
                        return Results.BadRequest(new { error_code = "INVALID_COURIER_ID" });
                    var eventType = string.IsNullOrWhiteSpace(event_type) ? null : event_type.Trim().ToUpperInvariant();
                    if (eventType is not null && eventType is not ("DESTINATION" or "SECRET_ACCESS"))
                        return Results.BadRequest(new { error_code = "INVALID_EVENT_TYPE" });
                    var normalizedAction = string.IsNullOrWhiteSpace(action) ? null : action.Trim().ToUpperInvariant();
                    if (normalizedAction is not null && normalizedAction is not ("VERIFIED" or "REVOKED" or "SECRET_READ"))
                        return Results.BadRequest(new { error_code = "INVALID_ACTION" });
                    var actorId = string.IsNullOrWhiteSpace(actor_id) ? null : actor_id.Trim();
                    if (actorId is { Length: > 128 } || (actorId is not null && actorId.Any(char.IsControl)))
                        return Results.BadRequest(new { error_code = "INVALID_ACTOR_ID" });
                    if (from_at.HasValue != to_at.HasValue || (from_at.HasValue && from_at.Value >= to_at!.Value))
                        return Results.BadRequest(new { error_code = "INVALID_DATE_RANGE" });
                    var fromAt = from_at?.ToUniversalTime();
                    var toAt = to_at?.ToUniversalTime();
                    if (!TryDecodeSearchCursor(cursor, courierId, eventType, normalizedAction,
                            actorId, fromAt, toAt, out var before))
                        return Results.BadRequest(new { error_code = "INVALID_CURSOR" });
                    var page = await service.SearchAsync(pageSize, courierId, eventType,
                        normalizedAction, actorId, fromAt, toAt, before, timeProvider.GetUtcNow(), cancellationToken);
                    var next = page.NextCursor is null ? null : EncodeSearchCursor(
                        courierId, eventType, normalizedAction, actorId, fromAt, toAt, page.NextCursor);
                    return Results.Ok(new CourierPayoutDestinationAuditPageResponse(
                        page.Items.Select(item => new CourierPayoutDestinationAuditItemResponse(
                            item.EventKey, item.EventType, item.Action, item.ActorId, item.OccurredAt,
                            item.EvidenceReference, item.OutcomeCode, item.MaskedIban)).ToArray(), next));
                })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }


    private sealed record SearchCursorPayload(
        string? CourierId, string? EventType, string? Action, string? ActorId,
        string? FromAt, string? ToAt, string OccurredAt, string EventKey);

    private static string EncodeSearchCursor(string? courierId, string? eventType, string? action,
        string? actorId, DateTimeOffset? fromAt, DateTimeOffset? toAt,
        CourierPayoutDestinationAuditCursor cursor)
    {
        var payload = new SearchCursorPayload(courierId, eventType, action, actorId,
            fromAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            toAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            cursor.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), cursor.EventKey);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeSearchCursor(string? encoded, string? courierId, string? eventType,
        string? action, string? actorId, DateTimeOffset? fromAt, DateTimeOffset? toAt,
        out CourierPayoutDestinationAuditCursor? cursor)
    {
        cursor = null;
        if (encoded is null) return true;
        if (encoded.Length is < 8 or > 1024) return false;
        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var payload = JsonSerializer.Deserialize<SearchCursorPayload>(Convert.FromBase64String(base64));
            if (payload is null || payload.CourierId != courierId || payload.EventType != eventType ||
                payload.Action != action || payload.ActorId != actorId ||
                payload.FromAt != fromAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ||
                payload.ToAt != toAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ||
                !DateTimeOffset.TryParse(payload.OccurredAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var occurredAt) || !IsEventKey(payload.EventKey))
                return false;
            cursor = new CourierPayoutDestinationAuditCursor(occurredAt, payload.EventKey);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { return false; }
    }

    private static bool IsCourierId(string value) => value.Length == 36 &&
        value.StartsWith("crr_", StringComparison.Ordinal) &&
        Guid.TryParseExact(value.AsSpan(4), "N", out _);

    private static string EncodeCursor(string courierId, CourierPayoutDestinationAuditCursor cursor)
    {
        var raw = string.Join("|", courierId,
            cursor.OccurredAt.UtcDateTime.Ticks.ToString(CultureInfo.InvariantCulture), cursor.EventKey);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(
        string? encoded, string courierId, out CourierPayoutDestinationAuditCursor? cursor)
    {
        cursor = null;
        if (encoded is null)
            return true;
        if (encoded.Length is < 8 or > 512)
            return false;
        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var parts = raw.Split('|');
            if (parts.Length != 3 || !string.Equals(parts[0], courierId, StringComparison.Ordinal) ||
                !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) ||
                ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks ||
                !IsEventKey(parts[2]))
                return false;
            cursor = new CourierPayoutDestinationAuditCursor(
                new DateTimeOffset(ticks, TimeSpan.Zero), parts[2]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsEventKey(string value)
    {
        if (value.StartsWith("d:", StringComparison.Ordinal) && value.Length == 22)
            return value.AsSpan(2).IndexOfAnyExceptInRange('0', '9') < 0;
        if (value.StartsWith("a:cpa_", StringComparison.Ordinal) && value.Length == 38)
            return value.AsSpan(6).IndexOfAnyExcept("0123456789abcdef") < 0;
        return false;
    }
}
