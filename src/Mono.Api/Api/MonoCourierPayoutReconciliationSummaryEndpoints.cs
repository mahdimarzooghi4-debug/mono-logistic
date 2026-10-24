using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierPayoutReconciliationSummaryBreakdownResponse(
    string ReconciliationCode, long WithdrawalCount, long AmountToman);
public sealed record CourierPayoutReconciliationSummaryResponse(
    string OutcomeCode, long TotalWithdrawalCount, long DiscrepancyCount, long TotalAmountToman,
    IReadOnlyList<CourierPayoutReconciliationSummaryBreakdownResponse> Breakdown);

public static class MonoCourierPayoutReconciliationSummaryEndpoints
{
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
        { "REQUESTED", "PROCESSING", "PAID", "REJECTED" };

    public static IEndpointRouteBuilder MapMonoCourierPayoutReconciliationSummaryEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/courier-payouts/reconciliation/summary",
            async Task<IResult> (HttpContext httpContext, PostgresCourierPayoutReconciliationSummaryService service,
                TimeProvider timeProvider, string? courier_id, string? status,
                DateTimeOffset? from_at, DateTimeOffset? to_at, CancellationToken cancellationToken) =>
            {
                httpContext.Response.Headers.CacheControl = "no-store";
                var courierId = string.IsNullOrWhiteSpace(courier_id) ? null : courier_id.Trim();
                if (courierId is not null && !IsCourierId(courierId))
                    return Results.BadRequest(new { error_code = "INVALID_COURIER_ID" });
                var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
                if (normalizedStatus is not null && !Statuses.Contains(normalizedStatus))
                    return Results.BadRequest(new { error_code = "INVALID_STATUS" });
                if (!from_at.HasValue || !to_at.HasValue || from_at.Value >= to_at.Value)
                    return Results.BadRequest(new { error_code = "INVALID_DATE_RANGE" });
                var summary = await service.ReadAsync(courierId, normalizedStatus,
                    from_at.Value.ToUniversalTime(), to_at.Value.ToUniversalTime(),
                    timeProvider.GetUtcNow(), cancellationToken);
                return Results.Ok(new CourierPayoutReconciliationSummaryResponse(
                    summary.OutcomeCode, summary.TotalWithdrawalCount, summary.DiscrepancyCount,
                    summary.TotalAmountToman, summary.Breakdown.Select(item =>
                        new CourierPayoutReconciliationSummaryBreakdownResponse(
                            item.ReconciliationCode, item.WithdrawalCount, item.AmountToman)).ToArray()));
            }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }

    private static bool IsCourierId(string value) => value.Length == 36 &&
        value.StartsWith("crr_", StringComparison.Ordinal) && Guid.TryParseExact(value.AsSpan(4), "N", out _);
}