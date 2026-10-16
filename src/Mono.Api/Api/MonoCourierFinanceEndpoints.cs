using System.Text.Json.Serialization;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record MonoCourierAvailableBalanceResponse(
    [property: JsonPropertyName("available_balance_toman")] long AvailableBalanceToman,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt);

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
        return endpoints;
    }
}
