using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierLivePresenceOperationsSearchItemResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("courier_id")] string CourierId,
    [property: System.Text.Json.Serialization.JsonPropertyName("status")] string Status,
    [property: System.Text.Json.Serialization.JsonPropertyName("zone_id")] string? ZoneId,
    [property: System.Text.Json.Serialization.JsonPropertyName("updated_at")] DateTimeOffset? UpdatedAt);
public sealed record CourierLivePresenceOperationsSearchPageResponse(
    IReadOnlyList<CourierLivePresenceOperationsSearchItemResponse> Items, string? NextCursor);

public static class MonoCourierLivePresenceOperationsSearchEndpoints
{
    private static readonly Regex ZoneIdPattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._~-]{0,127}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IEndpointRouteBuilder MapMonoCourierLivePresenceOperationsSearchEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/couriers/presence",
            async Task<IResult> (HttpContext context, PostgresCourierLivePresenceOperationsSearchService service,
                [FromQuery(Name = "limit")] int? limit,
                [FromQuery(Name = "status")] string? status,
                [FromQuery(Name = "zone_id")] string? zoneId,
                [FromQuery(Name = "updated_after")] DateTimeOffset? updatedAfter,
                [FromQuery(Name = "updated_before")] DateTimeOffset? updatedBefore,
                [FromQuery(Name = "cursor")] string? cursor,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var pageSize = limit ?? 50;
                if (pageSize is < 1 or > 100)
                    return Results.BadRequest(new { error_code = "INVALID_LIMIT" });
                status = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
                if (status is not (null or "ONLINE" or "OFFLINE"))
                    return Results.BadRequest(new { error_code = "INVALID_STATUS" });
                zoneId = string.IsNullOrWhiteSpace(zoneId) ? null : zoneId.Trim();
                if (zoneId is not null && !ZoneIdPattern.IsMatch(zoneId))
                    return Results.BadRequest(new { error_code = "INVALID_ZONE_ID" });
                if (updatedAfter.HasValue) updatedAfter = updatedAfter.Value.ToUniversalTime();
                if (updatedBefore.HasValue) updatedBefore = updatedBefore.Value.ToUniversalTime();
                if (updatedAfter.HasValue && updatedBefore.HasValue && updatedAfter >= updatedBefore)
                    return Results.BadRequest(new { error_code = "INVALID_DATE_RANGE" });
                if (!TryDecodeCursor(cursor, status, zoneId, updatedAfter, updatedBefore, out var beforeId))
                    return Results.BadRequest(new { error_code = "INVALID_CURSOR" });

                var page = await service.SearchAsync(pageSize, status, zoneId, updatedAfter,
                    updatedBefore, beforeId, cancellationToken);
                var nextCursor = page.NextCursor is null ? null : EncodeCursor(
                    status, zoneId, updatedAfter, updatedBefore, page.NextCursor);
                return Results.Ok(new CourierLivePresenceOperationsSearchPageResponse(
                    page.Items.Select(item => new CourierLivePresenceOperationsSearchItemResponse(
                        item.CourierId, item.Status, item.ZoneId, item.UpdatedAt)).ToArray(),
                    nextCursor));
            }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }

    private sealed record CursorPayload(
        string? Status, string? ZoneId, string? UpdatedAfter, string? UpdatedBefore, string? CourierId);

    private static string EncodeCursor(string? status, string? zoneId,
        DateTimeOffset? updatedAfter, DateTimeOffset? updatedBefore, string courierId)
    {
        var payload = new CursorPayload(status, zoneId, Format(updatedAfter), Format(updatedBefore), courierId);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(string? encoded, string? status, string? zoneId,
        DateTimeOffset? updatedAfter, DateTimeOffset? updatedBefore, out string? courierId)
    {
        courierId = null;
        if (encoded is null) return true;
        if (encoded.Length is < 8 or > 1024) return false;
        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(base64));
            if (payload is null || payload.Status != status || payload.ZoneId != zoneId ||
                payload.UpdatedAfter != Format(updatedAfter) || payload.UpdatedBefore != Format(updatedBefore) ||
                payload.CourierId is null || payload.CourierId.Length != 36 || !payload.CourierId.StartsWith("crr_", StringComparison.Ordinal) ||
                !Guid.TryParseExact(payload.CourierId.AsSpan(4), "N", out _))
                return false;
            courierId = payload.CourierId;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static string? Format(DateTimeOffset? value) =>
        value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
