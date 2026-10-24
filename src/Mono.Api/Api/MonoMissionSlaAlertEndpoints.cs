using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record MissionSlaAlertPageResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("items")] IReadOnlyList<MissionSlaAlert> Items,
    [property: System.Text.Json.Serialization.JsonPropertyName("next_cursor")] string? NextCursor,
    [property: System.Text.Json.Serialization.JsonPropertyName("evaluated_at")] DateTimeOffset EvaluatedAt);

public static class MonoMissionSlaAlertEndpoints
{
    private static readonly Regex MissionIdPattern = new(@"\Amis_[0-9a-f]{32}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IEndpointRouteBuilder MapMonoMissionSlaAlertEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/sla/delivery-alerts",
            async Task<IResult> (HttpContext context, PostgresMissionSlaAlertService service,
                [FromQuery(Name = "limit")] int? limit,
                [FromQuery(Name = "customer_id")] string? customerId,
                [FromQuery(Name = "minutes_ahead")] int? minutesAhead,
                [FromQuery(Name = "cursor")] string? cursor,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var size = limit ?? 50;
                var ahead = minutesAhead ?? 0;
                customerId = Clean(customerId);
                if (size is < 1 or > 100 || ahead is < 0 or > 10080)
                    return Results.BadRequest(new { error_code = "INVALID_FILTERS" });
                if (!TryDecodeCursor(cursor, customerId, ahead, out var evaluatedAt, out var before))
                    return Results.BadRequest(new { error_code = "INVALID_CURSOR" });
                try
                {
                    var page = await service.SearchAsync(size, customerId, ahead, evaluatedAt, before, cancellationToken);
                    var next = page.NextCursor is null ? null : EncodeCursor(
                        customerId, ahead, page.EvaluatedAt, page.NextCursor);
                    return Results.Ok(new MissionSlaAlertPageResponse(page.Items, next, page.EvaluatedAt));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new { error_code = "INVALID_FILTERS" });
                }
            }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }

    private sealed record CursorPayload(string? CustomerId, int MinutesAhead, string EvaluatedAt,
        string DueAt, string MissionId);

    private static string EncodeCursor(string? customerId, int minutesAhead, DateTimeOffset evaluatedAt,
        MissionSlaAlertCursor cursor) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new CursorPayload(
            customerId, minutesAhead, Format(evaluatedAt), Format(cursor.DueAt), cursor.MissionId))))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecodeCursor(string? encoded, string? customerId, int minutesAhead,
        out DateTimeOffset? evaluatedAt, out MissionSlaAlertCursor? before)
    {
        evaluatedAt = null;
        before = null;
        if (encoded is null) return true;
        if (encoded.Length is < 8 or > 2048) return false;
        try
        {
            var value = encoded.Replace('-', '+').Replace('_', '/');
            value += new string('=', (4 - value.Length % 4) % 4);
            var payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(value));
            if (payload is null || payload.CustomerId != customerId || payload.MinutesAhead != minutesAhead ||
                !DateTimeOffset.TryParse(payload.EvaluatedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var at) ||
                !DateTimeOffset.TryParse(payload.DueAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dueAt) ||
                payload.MissionId is null || !MissionIdPattern.IsMatch(payload.MissionId))
                return false;
            evaluatedAt = at;
            before = new MissionSlaAlertCursor(dueAt, payload.MissionId);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
