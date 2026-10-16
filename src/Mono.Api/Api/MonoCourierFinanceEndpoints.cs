using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record MonoCourierAvailableBalanceResponse(
    [property: JsonPropertyName("available_balance_toman")] long AvailableBalanceToman,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt);

public sealed record MonoCourierEarningHistoryItemResponse(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("amount_toman")] long AmountToman,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record MonoCourierEarningHistoryResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<MonoCourierEarningHistoryItemResponse> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public static class MonoCourierFinanceEndpoints
{
    public static IEndpointRouteBuilder MapMonoCourierFinanceEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/courier/v1/finance/balance", async Task<IResult> (
                IMonoCourierContext courier,
                PostgresCourierEarningLedger ledger,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var balance = await ledger.GetAvailableBalanceAsync(courier.CourierId,
                    timeProvider.GetUtcNow(), cancellationToken);
                return Results.Ok(new MonoCourierAvailableBalanceResponse(
                    balance.AmountToman, "TOMAN", balance.ObservedAt));
            })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapGet("/courier/v1/finance/earnings", async Task<IResult> (
                IMonoCourierContext courier,
                PostgresCourierEarningLedger ledger,
                int? limit,
                string? cursor,
                CancellationToken cancellationToken) =>
            {
                var pageSize = limit ?? 50;
                if (pageSize is < 1 or > 100)
                    return Results.BadRequest(new { error_code = "INVALID_LIMIT" });
                if (!TryDecodeCursor(cursor, out var beforeEntryId))
                    return Results.BadRequest(new { error_code = "INVALID_CURSOR" });

                var page = await ledger.GetEarningHistoryAsync(courier.CourierId,
                    pageSize, beforeEntryId, cancellationToken);
                var items = page.Items.Select(item => new MonoCourierEarningHistoryItemResponse(
                    item.MissionId, item.AmountToman, item.OccurredAt)).ToArray();
                return Results.Ok(new MonoCourierEarningHistoryResponse(items,
                    page.NextBeforeEntryId is long next ? EncodeCursor(next) : null));
            })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);
        return endpoints;
    }

    private static string EncodeCursor(long entryId)
    {
        var bytes = Encoding.UTF8.GetBytes(entryId.ToString(CultureInfo.InvariantCulture));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeCursor(string? cursor, out long? entryId)
    {
        entryId = null;
        if (cursor is null)
            return true;
        if (cursor.Length is 0 or > 128)
            return false;
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            if (!long.TryParse(decoded, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                || parsed <= 0)
                return false;
            entryId = parsed;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
