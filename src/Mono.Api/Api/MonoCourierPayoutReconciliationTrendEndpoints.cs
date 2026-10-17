using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierPayoutReconciliationTrendBucketResponse(
    DateOnly UtcDate, string ReconciliationCode, long WithdrawalCount, long AmountToman);
public sealed record CourierPayoutReconciliationTrendResponse(
    string OutcomeCode, long TotalWithdrawalCount, long DiscrepancyCount, long TotalAmountToman,
    IReadOnlyList<CourierPayoutReconciliationTrendBucketResponse> Buckets);

public static class MonoCourierPayoutReconciliationTrendEndpoints
{
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
        { "REQUESTED", "PROCESSING", "PAID", "REJECTED" };

    public static IEndpointRouteBuilder MapMonoCourierPayoutReconciliationTrendEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/courier-payouts/reconciliation/daily-trend",
            async Task<IResult> (HttpContext httpContext, PostgresCourierPayoutReconciliationTrendService service,
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
                var trend = await service.ReadAsync(courierId, normalizedStatus,
                    from_at.Value.ToUniversalTime(), to_at.Value.ToUniversalTime(),
                    timeProvider.GetUtcNow(), cancellationToken);
                return Results.Ok(new CourierPayoutReconciliationTrendResponse(
                    trend.OutcomeCode, trend.TotalWithdrawalCount, trend.DiscrepancyCount, trend.TotalAmountToman,
                    trend.Buckets.Select(item => new CourierPayoutReconciliationTrendBucketResponse(
                        item.UtcDate, item.ReconciliationCode, item.WithdrawalCount, item.AmountToman)).ToArray()));
            }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }

    private static bool IsCourierId(string value) => value.Length == 36 &&
        value.StartsWith("crr_", StringComparison.Ordinal) && Guid.TryParseExact(value.AsSpan(4), "N", out _);
}