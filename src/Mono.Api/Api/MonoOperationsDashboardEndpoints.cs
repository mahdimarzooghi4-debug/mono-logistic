using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record OperationsDashboardSummaryResponse(
    [property: JsonPropertyName("as_of")] DateTimeOffset AsOf,
    [property: JsonPropertyName("business_date")] DateOnly BusinessDate,
    [property: JsonPropertyName("time_zone")] string TimeZone,
    [property: JsonPropertyName("sla_horizon_minutes")] int SlaHorizonMinutes,
    [property: JsonPropertyName("active_mission_count")] long ActiveMissionCount,
    [property: JsonPropertyName("unassigned_mission_count")] long UnassignedMissionCount,
    [property: JsonPropertyName("online_courier_count")] long OnlineCourierCount,
    [property: JsonPropertyName("sla_at_risk_count")] long SlaAtRiskCount,
    [property: JsonPropertyName("sla_overdue_count")] long SlaOverdueCount,
    [property: JsonPropertyName("delivered_today_count")] long DeliveredTodayCount,
    [property: JsonPropertyName("on_time_delivered_today_count")] long OnTimeDeliveredTodayCount,
    [property: JsonPropertyName("on_time_delivery_rate_percent")] decimal? OnTimeDeliveryRatePercent)
{
    public static OperationsDashboardSummaryResponse From(OperationsDashboardSummary summary) => new(
        summary.AsOf, summary.BusinessDate, summary.TimeZone, summary.SlaHorizonMinutes,
        summary.ActiveMissionCount, summary.UnassignedMissionCount, summary.OnlineCourierCount,
        summary.SlaAtRiskCount, summary.SlaOverdueCount, summary.DeliveredTodayCount,
        summary.OnTimeDeliveredTodayCount, summary.OnTimeDeliveryRatePercent);
}

public static class MonoOperationsDashboardEndpoints
{
    public static IEndpointRouteBuilder MapMonoOperationsDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/dashboard/summary",
            async Task<IResult> (HttpContext context,
                PostgresOperationsDashboardSummaryService dashboard,
                [FromQuery(Name = "sla_horizon_minutes")] int? slaHorizonMinutes,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var horizon = slaHorizonMinutes ?? 15;
                if (horizon is < 0 or > 10080)
                    return Results.BadRequest(new { error_code = "INVALID_SLA_HORIZON" });
                try
                {
                    return Results.Ok(OperationsDashboardSummaryResponse.From(
                        await dashboard.GetAsync(horizon, cancellationToken)));
                }
                catch (ArgumentOutOfRangeException)
                {
                    return Results.BadRequest(new { error_code = "INVALID_SLA_HORIZON" });
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }
}
