using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierPayoutLedgerDetailEntryResponse(string AccountOwnerType, string AccountCode, string Currency, long DebitAmountToman, long CreditAmountToman, DateTimeOffset CreatedAt);
public sealed record CourierPayoutLedgerDetailTransactionResponse(string LedgerTransactionId, string Currency, DateTimeOffset OccurredAt, IReadOnlyList<CourierPayoutLedgerDetailEntryResponse> Entries);
public sealed record CourierPayoutLedgerDetailResponse(string OutcomeCode, string WithdrawalId, string? CourierId, long? AmountToman, string? LatestAction, IReadOnlyList<CourierPayoutLedgerDetailTransactionResponse> Transactions);

public static class MonoCourierPayoutLedgerDetailEndpoints
{
    public static IEndpointRouteBuilder MapMonoCourierPayoutLedgerDetailEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/courier-payouts/{withdrawalId}/ledger-detail",
            async Task<IResult> (string withdrawalId, HttpContext httpContext,
                PostgresCourierPayoutLedgerDetailReadService service, TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                if (!IsWithdrawalId(withdrawalId)) return Results.BadRequest(new { error_code = "INVALID_WITHDRAWAL_ID" });
                var detail = await service.ReadAsync(withdrawalId, timeProvider.GetUtcNow(), cancellationToken);
                return Results.Ok(new CourierPayoutLedgerDetailResponse(detail.OutcomeCode, detail.WithdrawalId,
                    detail.CourierId, detail.AmountToman, detail.LatestAction,
                    detail.Transactions.Select(tx => new CourierPayoutLedgerDetailTransactionResponse(
                        tx.LedgerTransactionId, tx.Currency, tx.OccurredAt,
                        tx.Entries.Select(entry => new CourierPayoutLedgerDetailEntryResponse(entry.AccountOwnerType,
                            entry.AccountCode, entry.Currency, entry.DebitAmountToman, entry.CreditAmountToman, entry.CreatedAt)).ToArray())).ToArray()));
            }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }
    private static bool IsWithdrawalId(string value) => value.Length == 36 &&
        value.StartsWith("wdr_", StringComparison.Ordinal) && Guid.TryParseExact(value.AsSpan(4), "N", out _);
}