using System.Globalization;
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

public sealed record MonoCourierLedgerHistoryItemResponse(
    [property: JsonPropertyName("ledger_entry_id")] long LedgerEntryId,
    [property: JsonPropertyName("reference_type")] string ReferenceType,
    [property: JsonPropertyName("reference_id")] string ReferenceId,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("amount_toman")] long AmountToman,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record MonoCourierLedgerHistoryResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<MonoCourierLedgerHistoryItemResponse> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor,
    [property: JsonPropertyName("total_debit_toman")] long TotalDebitAmountToman,
    [property: JsonPropertyName("total_credit_toman")] long TotalCreditAmountToman,
    [property: JsonPropertyName("opening_posted_balance_toman")] long OpeningPostedBalanceToman,
    [property: JsonPropertyName("closing_posted_balance_toman")] long ClosingPostedBalanceToman,
    [property: JsonPropertyName("from_at")] DateTimeOffset? FromAt,
    [property: JsonPropertyName("to_at_exclusive")] DateTimeOffset? ToAtExclusive);

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
        endpoints.MapGet("/courier/v1/finance/ledger", async Task<IResult> (
                IMonoCourierContext courier,
                PostgresCourierEarningLedger ledger,
                int? limit,
                string? cursor,
                DateTimeOffset? from_at,
                DateTimeOffset? to_at,
                CancellationToken cancellationToken) =>
            {
                var pageSize = limit ?? 50;
                if (pageSize is < 1 or > 100)
                    return Results.BadRequest(new { error_code = "INVALID_LIMIT" });
                if (from_at.HasValue != to_at.HasValue ||
                    (from_at.HasValue && from_at.Value >= to_at!.Value))
                    return Results.BadRequest(new { error_code = "INVALID_DATE_RANGE" });
                var fromAt = from_at?.ToUniversalTime();
                var toAt = to_at?.ToUniversalTime();
                if (!TryDecodeLedgerCursor(cursor, fromAt, toAt, out var beforeEntryId))
                    return Results.BadRequest(new { error_code = "INVALID_CURSOR" });

                var page = await ledger.GetLedgerHistoryAsync(courier.CourierId,
                    pageSize, beforeEntryId, cancellationToken, fromAt, toAt);
                var items = page.Items.Select(item => new MonoCourierLedgerHistoryItemResponse(
                    item.LedgerEntryId, item.ReferenceType, item.ReferenceId,
                    item.DebitAmountToman > 0 ? "DEBIT" : "CREDIT",
                    item.DebitAmountToman > 0 ? item.DebitAmountToman : item.CreditAmountToman,
                    item.OccurredAt)).ToArray();
                return Results.Ok(new MonoCourierLedgerHistoryResponse(items,
                    page.NextBeforeEntryId is long next ? EncodeLedgerCursor(next, fromAt, toAt) : null,
                    page.TotalDebitAmountToman, page.TotalCreditAmountToman,
                    page.OpeningPostedBalanceToman, page.ClosingPostedBalanceToman, fromAt, toAt));
            })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        return endpoints;
    }

    private static string EncodeCursor(long entryId)
    {
        var bytes = Encoding.UTF8.GetBytes(entryId.ToString(CultureInfo.InvariantCulture));
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string EncodeLedgerCursor(
        long entryId, DateTimeOffset? fromAt, DateTimeOffset? toAt)
    {
        var value = string.Join("|", "ledger-v1",
            fromAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "*",
            toAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "*",
            entryId.ToString(CultureInfo.InvariantCulture));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecodeLedgerCursor(
        string? cursor, DateTimeOffset? fromAt, DateTimeOffset? toAt, out long? entryId)
    {
        entryId = null;
        if (cursor is null)
            return true;
        if (cursor.Length is 0 or > 512)
            return false;
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 += new string('=', (4 - base64.Length % 4) % 4);
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(base64)).Split('|');
            if (parts.Length != 4 || parts[0] != "ledger-v1" ||
                parts[1] != (fromAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "*") ||
                parts[2] != (toAt?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "*") ||
                !long.TryParse(parts[3], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ||
                parsed <= 0)
                return false;
            entryId = parsed;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
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
