using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record RoutePlanOperationsReadAuditItemResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("audit_id")] string AuditId,
    [property: System.Text.Json.Serialization.JsonPropertyName("actor_id")] string ActorId,
    [property: System.Text.Json.Serialization.JsonPropertyName("route_id")] string? RouteId,
    [property: System.Text.Json.Serialization.JsonPropertyName("action")] string Action,
    [property: System.Text.Json.Serialization.JsonPropertyName("version")] int? Version,
    [property: System.Text.Json.Serialization.JsonPropertyName("outcome")] string Outcome,
    [property: System.Text.Json.Serialization.JsonPropertyName("result_count")] int ResultCount,
    [property: System.Text.Json.Serialization.JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);
public sealed record RoutePlanOperationsReadAuditPageResponse(
    IReadOnlyList<RoutePlanOperationsReadAuditItemResponse> Items, string? NextCursor);

public static class MonoRoutePlanReadAuditEndpoints
{
    public static IEndpointRouteBuilder MapMonoRoutePlanReadAuditEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/routes/plan-read-audit",
            async Task<IResult> (HttpContext context, PostgresRoutePlanOperationsReadAuditService audit,
                [FromQuery(Name = "route_id")] string? routeId,
                [FromQuery(Name = "action")] string? action,
                [FromQuery(Name = "outcome")] string? outcome,
                [FromQuery(Name = "from_at")] DateTimeOffset? fromAt,
                [FromQuery(Name = "to_at")] DateTimeOffset? toAt,
                [FromQuery(Name = "limit")] int? limit,
                [FromQuery(Name = "cursor")] string? cursor,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var pageSize = limit ?? 50;
                routeId = string.IsNullOrWhiteSpace(routeId) ? null : routeId.Trim();
                action = string.IsNullOrWhiteSpace(action) ? null : action.Trim().ToUpperInvariant();
                outcome = string.IsNullOrWhiteSpace(outcome) ? null : outcome.Trim().ToUpperInvariant();
                if (pageSize is < 1 or > 100 ||
                    (routeId is not null && !PostgresRoutePlanOperationsReadAuditService.IsRouteIdValid(routeId)) ||
                    (action is not null && action is not (
                        PostgresRoutePlanOperationsReadAuditService.CurrentPlanRead or
                        PostgresRoutePlanOperationsReadAuditService.PlanVersionRead or
                        PostgresRoutePlanOperationsReadAuditService.AuditInspection)) ||
                    (outcome is not null && outcome is not ("FOUND" or "NOT_FOUND" or "EMPTY")) ||
                    !fromAt.HasValue || !toAt.HasValue || fromAt.Value >= toAt.Value)
                    return Results.BadRequest(new { error_code = "INVALID_FILTERS" });

                var from = fromAt.Value.ToUniversalTime();
                var to = toAt.Value.ToUniversalTime();
                if (!TryDecodeCursor(cursor, routeId, action, outcome, from, to, out var before))
                    return Results.BadRequest(new { error_code = "INVALID_CURSOR" });
                var page = await audit.ListAsync(routeId, action, outcome, from, to,
                    pageSize, before, cancellationToken);
                var nextCursor = page!.NextCursor is null ? null : EncodeCursor(
                    routeId, action, outcome, from, to, page.NextCursor);
                return Results.Ok(new RoutePlanOperationsReadAuditPageResponse(
                    page.Items.Select(item => new RoutePlanOperationsReadAuditItemResponse(
                        item.AuditId, item.ActorId, item.RouteId, item.Action,
                        item.Version, item.Outcome, item.ResultCount, item.OccurredAt)).ToArray(),
                    nextCursor));
            }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }

    private sealed record CursorPayload(
        string? RouteId, string? Action, string? Outcome, string From, string To,
        string At, string AuditId);

    private static string EncodeCursor(string? routeId, string? action, string? outcome,
        DateTimeOffset from, DateTimeOffset to, RoutePlanOperationsReadAuditCursor cursor)
    {
        var payload = new CursorPayload(routeId, action, outcome, Format(from), Format(to),
            Format(cursor.OccurredAt), cursor.AuditId);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(string? encoded, string? routeId, string? action,
        string? outcome, DateTimeOffset from, DateTimeOffset to,
        out RoutePlanOperationsReadAuditCursor? cursor)
    {
        cursor = null;
        if (encoded is null) return true;
        if (encoded.Length is < 8 or > 1024) return false;
        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(base64));
            if (payload is null || payload.RouteId != routeId || payload.Action != action ||
                payload.Outcome != outcome || payload.From != Format(from) || payload.To != Format(to) ||
                !DateTimeOffset.TryParse(payload.At, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var occurredAt) ||
                !PostgresRoutePlanOperationsReadAuditService.IsValidAuditCursor(payload.AuditId))
                return false;
            cursor = new RoutePlanOperationsReadAuditCursor(occurredAt, payload.AuditId);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
