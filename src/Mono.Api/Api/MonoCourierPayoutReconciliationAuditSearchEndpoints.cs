using System.Globalization;
using System.Text;
using System.Text.Json;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierPayoutReconciliationAuditItemResponse(
    string ReadAuditId, string ActorId, string? CourierIdFilter, string? StatusFilter,
    string? ReconciliationCodeFilter, DateTimeOffset CreatedFrom, DateTimeOffset CreatedTo,
    bool OnlyDiscrepancies, int PageSize, int ResultCount, string OutcomeCode,
    DateTimeOffset OccurredAt);
public sealed record CourierPayoutReconciliationAuditPageResponse(
    IReadOnlyList<CourierPayoutReconciliationAuditItemResponse> Items, string? NextCursor);

public static class MonoCourierPayoutReconciliationAuditSearchEndpoints
{
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
        { "REQUESTED", "PROCESSING", "PAID", "REJECTED" };
    private static readonly HashSet<string> ReconciliationCodes = new(StringComparer.Ordinal)
        { "MATCHED", "MISSING_PAYOUT", "UNEXPECTED_PAYOUT", "MULTIPLE_PAYOUTS", "LEDGER_MISMATCH" };
    private static readonly HashSet<string> Outcomes = new(StringComparer.Ordinal) { "FOUND", "EMPTY" };

    public static IEndpointRouteBuilder MapMonoCourierPayoutReconciliationAuditSearchEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/courier-payouts/reconciliation-audit",
            async Task<IResult> (HttpContext httpContext,
                PostgresCourierPayoutReconciliationAuditSearchService service,
                TimeProvider timeProvider, int? limit, string? actor_id, string? courier_id,
                string? status, string? reconciliation_code, string? outcome,
                bool? only_discrepancies, DateTimeOffset? from_at, DateTimeOffset? to_at,
                string? cursor, CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                var pageSize = limit ?? 50;
                if (pageSize is < 1 or > 100)
                    return Results.BadRequest(new { error_code = "INVALID_LIMIT" });
                var actor = string.IsNullOrWhiteSpace(actor_id) ? null : actor_id.Trim();
                if (actor is { Length: > 128 })
                    return Results.BadRequest(new { error_code = "INVALID_ACTOR_ID" });
                var courier = string.IsNullOrWhiteSpace(courier_id) ? null : courier_id.Trim();
                if (courier is not null && !IsCourierId(courier))
                    return Results.BadRequest(new { error_code = "INVALID_COURIER_ID" });
                var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
                if (normalizedStatus is not null && !Statuses.Contains(normalizedStatus))
                    return Results.BadRequest(new { error_code = "INVALID_STATUS" });
                var normalizedCode = string.IsNullOrWhiteSpace(reconciliation_code)
                    ? null : reconciliation_code.Trim().ToUpperInvariant();
                if (normalizedCode is not null && !ReconciliationCodes.Contains(normalizedCode))
                    return Results.BadRequest(new { error_code = "INVALID_RECONCILIATION_CODE" });
                var normalizedOutcome = string.IsNullOrWhiteSpace(outcome) ? null : outcome.Trim().ToUpperInvariant();
                if (normalizedOutcome is not null && !Outcomes.Contains(normalizedOutcome))
                    return Results.BadRequest(new { error_code = "INVALID_OUTCOME" });
                if (!from_at.HasValue || !to_at.HasValue || from_at.Value >= to_at.Value)
                    return Results.BadRequest(new { error_code = "INVALID_DATE_RANGE" });
                var fromAt = from_at.Value.ToUniversalTime();
                var toAt = to_at.Value.ToUniversalTime();
                if (!TryDecodeCursor(cursor, actor, courier, normalizedStatus, normalizedCode,
                        normalizedOutcome, only_discrepancies, fromAt, toAt, out var before))
                    return Results.BadRequest(new { error_code = "INVALID_CURSOR" });
                var page = await service.ReadAsync(pageSize, actor, courier, normalizedStatus,
                    normalizedCode, normalizedOutcome, only_discrepancies, fromAt, toAt,
                    before, timeProvider.GetUtcNow(), cancellationToken);
                var nextCursor = page.NextCursor is null ? null : EncodeCursor(actor, courier,
                    normalizedStatus, normalizedCode, normalizedOutcome, only_discrepancies,
                    fromAt, toAt, page.NextCursor);
                return Results.Ok(new CourierPayoutReconciliationAuditPageResponse(
                    page.Items.Select(item => new CourierPayoutReconciliationAuditItemResponse(
                        item.ReadAuditId, item.ActorId, item.CourierIdFilter, item.StatusFilter,
                        item.ReconciliationCodeFilter, item.CreatedFrom, item.CreatedTo,
                        item.OnlyDiscrepancies, item.PageSize, item.ResultCount, item.OutcomeCode,
                        item.OccurredAt)).ToArray(), nextCursor));
            }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }

    private sealed record CursorPayload(
        string? Actor, string? Courier, string? Status, string? ReconciliationCode,
        string? Outcome, bool? OnlyDiscrepancies, string From, string To, string At, string Id);

    private static string EncodeCursor(string? actor, string? courier, string? status,
        string? reconciliationCode, string? outcome, bool? onlyDiscrepancies,
        DateTimeOffset from, DateTimeOffset to, CourierPayoutReconciliationAuditCursor cursor)
    {
        var payload = new CursorPayload(actor, courier, status, reconciliationCode, outcome,
            onlyDiscrepancies, from.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            to.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            cursor.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            cursor.ReadAuditId);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(string? encoded, string? actor, string? courier,
        string? status, string? reconciliationCode, string? outcome, bool? onlyDiscrepancies,
        DateTimeOffset from, DateTimeOffset to, out CourierPayoutReconciliationAuditCursor? cursor)
    {
        cursor = null;
        if (encoded is null) return true;
        if (encoded.Length is < 8 or > 1024) return false;
        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(base64));
            if (payload is null || payload.Actor != actor || payload.Courier != courier ||
                payload.Status != status || payload.ReconciliationCode != reconciliationCode ||
                payload.Outcome != outcome || payload.OnlyDiscrepancies != onlyDiscrepancies ||
                payload.From != from.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ||
                payload.To != to.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ||
                !DateTimeOffset.TryParse(payload.At, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var at) || !IsReadAuditId(payload.Id))
                return false;
            cursor = new CourierPayoutReconciliationAuditCursor(at, payload.Id);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { return false; }
    }

    private static bool IsCourierId(string value) => value.Length == 36 &&
        value.StartsWith("crr_", StringComparison.Ordinal) &&
        Guid.TryParseExact(value.AsSpan(4), "N", out _);
    private static bool IsReadAuditId(string? value) => value is { Length: 36 } &&
        value.StartsWith("cpr_", StringComparison.Ordinal) &&
        Guid.TryParseExact(value.AsSpan(4), "N", out _);
}
