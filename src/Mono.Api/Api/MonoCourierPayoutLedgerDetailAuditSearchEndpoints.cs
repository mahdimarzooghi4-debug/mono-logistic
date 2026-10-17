using System.Globalization;
using System.Text;
using System.Text.Json;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierPayoutLedgerDetailAuditItemResponse(
    string ReadAuditId, string ActorId, string WithdrawalId, string OutcomeCode,
    int TransactionCount, int EntryCount, DateTimeOffset OccurredAt);
public sealed record CourierPayoutLedgerDetailAuditPageResponse(
    IReadOnlyList<CourierPayoutLedgerDetailAuditItemResponse> Items, string? NextCursor);

public static class MonoCourierPayoutLedgerDetailAuditSearchEndpoints
{
    private static readonly HashSet<string> Outcomes = new(StringComparer.Ordinal)
        { "FOUND", "PAYOUT_NOT_POSTED", "WITHDRAWAL_NOT_FOUND" };

    public static IEndpointRouteBuilder MapMonoCourierPayoutLedgerDetailAuditSearchEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/courier-payouts/ledger-detail-audit",
            async Task<IResult> (HttpContext httpContext, PostgresCourierPayoutLedgerDetailAuditSearchService service,
                TimeProvider timeProvider, int? limit, string? actor_id, string? withdrawal_id, string? outcome,
                DateTimeOffset? from_at, DateTimeOffset? to_at, string? cursor, CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                var pageSize = limit ?? 50;
                if (pageSize is < 1 or > 100) return Results.BadRequest(new { error_code = "INVALID_LIMIT" });
                var actor = string.IsNullOrWhiteSpace(actor_id) ? null : actor_id.Trim();
                if (actor is { Length: > 128 }) return Results.BadRequest(new { error_code = "INVALID_ACTOR_ID" });
                var withdrawal = string.IsNullOrWhiteSpace(withdrawal_id) ? null : withdrawal_id.Trim();
                if (withdrawal is not null && !IsWithdrawalId(withdrawal))
                    return Results.BadRequest(new { error_code = "INVALID_WITHDRAWAL_ID" });
                var normalizedOutcome = string.IsNullOrWhiteSpace(outcome) ? null : outcome.Trim().ToUpperInvariant();
                if (normalizedOutcome is not null && !Outcomes.Contains(normalizedOutcome))
                    return Results.BadRequest(new { error_code = "INVALID_OUTCOME" });
                if (!from_at.HasValue || !to_at.HasValue || from_at.Value >= to_at.Value)
                    return Results.BadRequest(new { error_code = "INVALID_DATE_RANGE" });
                var fromAt = from_at.Value.ToUniversalTime();
                var toAt = to_at.Value.ToUniversalTime();
                if (!TryDecodeCursor(cursor, actor, withdrawal, normalizedOutcome, fromAt, toAt, out var before))
                    return Results.BadRequest(new { error_code = "INVALID_CURSOR" });
                var page = await service.ReadAsync(pageSize, actor, withdrawal, normalizedOutcome, fromAt, toAt,
                    before, timeProvider.GetUtcNow(), cancellationToken);
                var nextCursor = page.NextCursor is null ? null :
                    EncodeCursor(actor, withdrawal, normalizedOutcome, fromAt, toAt, page.NextCursor);
                return Results.Ok(new CourierPayoutLedgerDetailAuditPageResponse(
                    page.Items.Select(item => new CourierPayoutLedgerDetailAuditItemResponse(
                        item.ReadAuditId, item.ActorId, item.WithdrawalId, item.OutcomeCode,
                        item.TransactionCount, item.EntryCount, item.OccurredAt)).ToArray(), nextCursor));
            }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }

    private sealed record CursorPayload(string? Actor, string? Withdrawal, string? Outcome, string From, string To, string At, string Id);
    private static string EncodeCursor(string? actor, string? withdrawal, string? outcome, DateTimeOffset from, DateTimeOffset to, CourierPayoutLedgerDetailAuditCursor cursor)
    {
        var payload = new CursorPayload(actor, withdrawal, outcome, from.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            to.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), cursor.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), cursor.ReadAuditId);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(string? encoded, string? actor, string? withdrawal, string? outcome,
        DateTimeOffset from, DateTimeOffset to, out CourierPayoutLedgerDetailAuditCursor? cursor)
    {
        cursor = null;
        if (encoded is null) return true;
        if (encoded.Length is < 8 or > 1024) return false;
        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(base64));
            if (payload is null || payload.Actor != actor || payload.Withdrawal != withdrawal || payload.Outcome != outcome ||
                payload.From != from.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ||
                payload.To != to.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ||
                !DateTimeOffset.TryParse(payload.At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ||
                payload.Id is null || payload.Id.Length != 36 || !payload.Id.StartsWith("cpl_", StringComparison.Ordinal) ||
                !Guid.TryParseExact(payload.Id.AsSpan(4), "N", out _)) return false;
            cursor = new CourierPayoutLedgerDetailAuditCursor(at, payload.Id);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { return false; }
    }

    private static bool IsWithdrawalId(string value) => value.Length == 36 &&
        value.StartsWith("wdr_", StringComparison.Ordinal) && Guid.TryParseExact(value.AsSpan(4), "N", out _);
}