using System.Globalization;
using System.Text;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoCourierWithdrawalOperationsSearchEndpoints
{
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
    {
        "REQUESTED", "PROCESSING", "PAID", "REJECTED"
    };

    public static IEndpointRouteBuilder MapMonoCourierWithdrawalOperationsSearchEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/courier-withdrawals/search",
                async Task<IResult> (
                    HttpContext httpContext,
                    PostgresCourierWithdrawalOperationsSearchService service,
                    TimeProvider timeProvider,
                    int? limit,
                    string? status,
                    string? courier_id,
                    DateTimeOffset? from_at,
                    DateTimeOffset? to_at,
                    long? min_amount_toman,
                    long? max_amount_toman,
                    string? cursor,
                    CancellationToken cancellationToken) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var pageSize = limit ?? 50;
                    if (pageSize is < 1 or > 100)
                        return Results.BadRequest(new { error_code = "INVALID_LIMIT" });

                    var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
                    if (normalizedStatus is not null && !Statuses.Contains(normalizedStatus))
                        return Results.BadRequest(new { error_code = "INVALID_STATUS" });
                    var courierId = string.IsNullOrWhiteSpace(courier_id) ? null : courier_id.Trim();
                    if (courierId is not null && !IsCourierId(courierId))
                        return Results.BadRequest(new { error_code = "INVALID_COURIER_ID" });
                    if (from_at.HasValue != to_at.HasValue ||
                        (from_at.HasValue && from_at.Value >= to_at!.Value))
                        return Results.BadRequest(new { error_code = "INVALID_DATE_RANGE" });
                    if (min_amount_toman < 0 || max_amount_toman < 0 ||
                        (min_amount_toman.HasValue && max_amount_toman.HasValue &&
                         min_amount_toman > max_amount_toman))
                        return Results.BadRequest(new { error_code = "INVALID_AMOUNT_RANGE" });
                    var fromAt = from_at?.ToUniversalTime();
                    var toAt = to_at?.ToUniversalTime();
                    if (!TryDecodeCursor(cursor, normalizedStatus, courierId, fromAt, toAt,
                            min_amount_toman, max_amount_toman, out var beforeSequence))
                        return Results.BadRequest(new { error_code = "INVALID_CURSOR" });

                    var page = await service.SearchAsync(normalizedStatus, courierId, pageSize, beforeSequence,
                        timeProvider.GetUtcNow(), cancellationToken, fromAt, toAt,
                        min_amount_toman, max_amount_toman);
                    var nextCursor = page.NextBeforeSequence is long next
                        ? EncodeCursor(next, normalizedStatus, courierId, fromAt, toAt,
                            min_amount_toman, max_amount_toman)
                        : null;
                    var items = page.Items.Select(item => new OperationsCourierWithdrawalItemResponse(
                        item.WithdrawalId, item.CourierId, item.AmountToman, item.MaskedIban,
                        item.Status, item.CreatedAt, item.ReasonCode, item.BankReference)).ToArray();
                    return Results.Ok(new CourierWithdrawalPageResponse<OperationsCourierWithdrawalItemResponse>(
                        items, nextCursor));
                })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }

    private static bool IsCourierId(string value) => value.Length == 36 &&
        value.StartsWith("crr_", StringComparison.Ordinal) &&
        Guid.TryParseExact(value.AsSpan(4), "N", out _);

    private static string EncodeCursor(
        long sequence, string? status, string? courierId,
        DateTimeOffset? fromAt, DateTimeOffset? toAt, long? minAmountToman, long? maxAmountToman)
    {
        var value = string.Join("|", "withdrawals-ops-v2", status ?? "*", courierId ?? "*",
            fromAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "*",
            toAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "*",
            minAmountToman?.ToString(CultureInfo.InvariantCulture) ?? "*",
            maxAmountToman?.ToString(CultureInfo.InvariantCulture) ?? "*",
            sequence.ToString(CultureInfo.InvariantCulture));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(
        string? encoded, string? status, string? courierId,
        DateTimeOffset? fromAt, DateTimeOffset? toAt, long? minAmountToman, long? maxAmountToman,
        out long? sequence)
    {
        sequence = null;
        if (encoded is null)
            return true;
        if (encoded.Length is < 8 or > 256)
            return false;
        try
        {
            var base64 = encoded.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|');
            if (parts.Length != 8 || parts[0] != "withdrawals-ops-v2" ||
                parts[1] != (status ?? "*") || parts[2] != (courierId ?? "*") ||
                parts[3] != (fromAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "*") ||
                parts[4] != (toAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "*") ||
                parts[5] != (minAmountToman?.ToString(CultureInfo.InvariantCulture) ?? "*") ||
                parts[6] != (maxAmountToman?.ToString(CultureInfo.InvariantCulture) ?? "*") ||
                !long.TryParse(parts[7], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                parsed <= 0)
                return false;
            sequence = parsed;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
