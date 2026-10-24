using System.Globalization;
using System.Text;
using System.Text.Json;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierPayoutReconciliationItemResponse(
    string WithdrawalId, string CourierId, long AmountToman, string LatestAction,
    DateTimeOffset CreatedAt, long PayoutTransactionCount, long LedgerEntryCount,
    long MatchingCourierDebitCount, long MatchingClearingCreditCount, string ReconciliationCode);

public sealed record CourierPayoutReconciliationPageResponse(
    IReadOnlyList<CourierPayoutReconciliationItemResponse> Items, string? NextCursor);

public static class MonoCourierPayoutReconciliationEndpoints
{
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
        { "REQUESTED", "PROCESSING", "PAID", "REJECTED" };
    private static readonly HashSet<string> ReconciliationCodes = new(StringComparer.Ordinal)
        { "MATCHED", "MISSING_PAYOUT", "UNEXPECTED_PAYOUT", "MULTIPLE_PAYOUTS", "LEDGER_MISMATCH" };

    public static IEndpointRouteBuilder MapMonoCourierPayoutReconciliationEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/courier-payouts/reconciliation",
                async Task<IResult> (HttpContext httpContext,
                    PostgresCourierPayoutReconciliationService service,
                    TimeProvider timeProvider, int? limit, string? courier_id, string? status,
                    DateTimeOffset? from_at, DateTimeOffset? to_at, bool? only_discrepancies,
                    string? reconciliation_code, string? cursor, CancellationToken cancellationToken) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var pageSize = limit ?? 50;
                    if (pageSize is < 1 or > 100)
                        return Results.BadRequest(new { error_code = "INVALID_LIMIT" });
                    var courierId = string.IsNullOrWhiteSpace(courier_id) ? null : courier_id.Trim();
                    if (courierId is not null && !IsCourierId(courierId))
                        return Results.BadRequest(new { error_code = "INVALID_COURIER_ID" });
                    var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
                    if (normalizedStatus is not null && !Statuses.Contains(normalizedStatus))
                        return Results.BadRequest(new { error_code = "INVALID_STATUS" });
                    if (!from_at.HasValue || !to_at.HasValue || from_at.Value >= to_at.Value)
                        return Results.BadRequest(new { error_code = "INVALID_DATE_RANGE" });
                    var fromAt = from_at.Value.ToUniversalTime();
                    var toAt = to_at.Value.ToUniversalTime();
                    var onlyDiscrepancies = only_discrepancies ?? false;
                    var reconciliationCode = string.IsNullOrWhiteSpace(reconciliation_code)
                        ? null : reconciliation_code.Trim().ToUpperInvariant();
                    if (reconciliationCode is not null && !ReconciliationCodes.Contains(reconciliationCode))
                        return Results.BadRequest(new { error_code = "INVALID_RECONCILIATION_CODE" });
                    if (!TryDecodeCursor(cursor, courierId, normalizedStatus, reconciliationCode, fromAt, toAt,
                            onlyDiscrepancies, out var before))
                        return Results.BadRequest(new { error_code = "INVALID_CURSOR" });
                    var page = await service.ReadAsync(pageSize, courierId, normalizedStatus, reconciliationCode,
                        fromAt, toAt, onlyDiscrepancies, before, timeProvider.GetUtcNow(), cancellationToken);
                    var nextCursor = page.NextCursor is null ? null : EncodeCursor(
                        courierId, normalizedStatus, reconciliationCode, fromAt, toAt,
                        onlyDiscrepancies, page.NextCursor);
                    return Results.Ok(new CourierPayoutReconciliationPageResponse(
                        page.Items.Select(item => new CourierPayoutReconciliationItemResponse(
                            item.WithdrawalId, item.CourierId, item.AmountToman, item.LatestAction,
                            item.CreatedAt, item.PayoutTransactionCount, item.LedgerEntryCount,
                            item.MatchingCourierDebitCount, item.MatchingClearingCreditCount,
                            item.ReconciliationCode)).ToArray(), nextCursor));
                })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }

    private sealed record CursorPayload(
        string? CourierId, string? Status, string? ReconciliationCode, string FromAt, string ToAt,
        bool OnlyDiscrepancies, string CreatedAt, string WithdrawalId);

    private static string EncodeCursor(string? courierId, string? status, string? reconciliationCode,
        DateTimeOffset fromAt, DateTimeOffset toAt, bool onlyDiscrepancies,
        CourierPayoutReconciliationCursor cursor)
    {
        var payload = new CursorPayload(courierId, status, reconciliationCode,
            fromAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            toAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), onlyDiscrepancies,
            cursor.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), cursor.WithdrawalId);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(string? encoded, string? courierId, string? status,
        string? reconciliationCode, DateTimeOffset fromAt, DateTimeOffset toAt,
        bool onlyDiscrepancies, out CourierPayoutReconciliationCursor? cursor)
    {
        cursor = null;
        if (encoded is null) return true;
        if (encoded.Length is < 8 or > 1024) return false;
        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(base64));
            if (payload is null || payload.CourierId != courierId || payload.Status != status ||
                payload.ReconciliationCode != reconciliationCode ||
                payload.FromAt != fromAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ||
                payload.ToAt != toAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ||
                payload.OnlyDiscrepancies != onlyDiscrepancies ||
                !DateTimeOffset.TryParse(payload.CreatedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var createdAt) || !IsWithdrawalId(payload.WithdrawalId))
                return false;
            cursor = new CourierPayoutReconciliationCursor(createdAt, payload.WithdrawalId);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { return false; }
    }

    private static bool IsCourierId(string value) => value.Length == 36 &&
        value.StartsWith("crr_", StringComparison.Ordinal) &&
        Guid.TryParseExact(value.AsSpan(4), "N", out _);
    private static bool IsWithdrawalId(string value) => value.Length == 36 &&
        value.StartsWith("wdr_", StringComparison.Ordinal) &&
        Guid.TryParseExact(value.AsSpan(4), "N", out _);
}
