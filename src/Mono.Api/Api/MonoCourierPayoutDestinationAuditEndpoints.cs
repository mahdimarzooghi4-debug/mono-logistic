using System.Globalization;
using System.Text;
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
        return endpoints;
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
