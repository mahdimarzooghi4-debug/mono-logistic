using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record OperationsDashboardSummary(
    DateTimeOffset AsOf,
    DateOnly BusinessDate,
    string TimeZone,
    int SlaHorizonMinutes,
    long ActiveMissionCount,
    long UnassignedMissionCount,
    long OnlineCourierCount,
    long SlaAtRiskCount,
    long SlaOverdueCount,
    long DeliveredTodayCount,
    long OnTimeDeliveredTodayCount,
    decimal? OnTimeDeliveryRatePercent);

/// <summary>Authoritative, point-in-time dashboard aggregates over existing Mission and presence data.</summary>
public sealed class PostgresOperationsDashboardSummaryService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    PostgresMissionOperationsReadAuditService readAudit,
    TimeProvider? timeProvider = null)
{
    private const string TehranTimeZoneId = "Asia/Tehran";
    private static readonly TimeZoneInfo TehranTimeZone = TimeZoneInfo.FindSystemTimeZoneById(TehranTimeZoneId);
    private static readonly string[] ActiveStatuses =
    [
        "PROMISED", "SEARCHING_COURIER", "COURIER_ASSIGNED", "ARRIVED_PICKUP",
        "HANDED_TO_COURIER", "PICKED_UP", "IN_TRANSIT", "ARRIVED_DROPOFF"
    ];

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<OperationsDashboardSummary> GetAsync(
        int slaHorizonMinutes,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        if (slaHorizonMinutes is < 0 or > 10080)
            throw new ArgumentOutOfRangeException(nameof(slaHorizonMinutes));

        var asOf = _timeProvider.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(asOf, TehranTimeZone);
        var businessDate = DateOnly.FromDateTime(localNow.DateTime);
        var localStart = businessDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localEnd = businessDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var dayStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localStart, TehranTimeZone));
        var dayEnd = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localEnd, TehranTimeZone));

        const string sql = """
            SELECT
                (SELECT count(*) FROM missions m WHERE m.status = ANY(@active_statuses)),
                (SELECT count(*) FROM missions m
                 WHERE m.status = ANY(@active_statuses)
                   AND NOT EXISTS (
                       SELECT 1 FROM mission_courier_offers offer
                       WHERE offer.mission_id = m.mission_id AND offer.status = 'ACCEPTED')),
                (SELECT count(*) FROM couriers courier
                 JOIN courier_live_presence presence ON presence.courier_id = courier.courier_id
                 WHERE courier.status = 'ACTIVE' AND presence.is_online),
                (SELECT count(*) FROM missions m
                 WHERE m.status = ANY(@active_statuses)
                   AND m.committed_delivery_at > @as_of
                   AND m.committed_delivery_at <= @risk_through),
                (SELECT count(*) FROM missions m
                 WHERE m.status = ANY(@active_statuses)
                   AND m.committed_delivery_at <= @as_of),
                (SELECT count(*) FROM outbox_events delivered_event
                 WHERE delivered_event.aggregate_type = 'mission'
                   AND delivered_event.event_type = 'mission.delivered'
                   AND delivered_event.occurred_at >= @day_start AND delivered_event.occurred_at < @day_end),
                (SELECT count(*) FROM outbox_events delivered_event
                 JOIN missions mission
                   ON mission.mission_id = delivered_event.aggregate_id
                  AND mission.customer_id = delivered_event.customer_id
                 WHERE delivered_event.aggregate_type = 'mission'
                   AND delivered_event.event_type = 'mission.delivered'
                   AND delivered_event.occurred_at >= @day_start AND delivered_event.occurred_at < @day_end
                   AND delivered_event.occurred_at <= mission.committed_delivery_at);
            """;

        long activeMissions;
        long unassignedMissions;
        long onlineCouriers;
        long atRisk;
        long overdue;
        long deliveredToday;
        long onTimeDeliveredToday;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("active_statuses", NpgsqlDbType.Array | NpgsqlDbType.Text, ActiveStatuses);
            command.Parameters.AddWithValue("risk_through", NpgsqlDbType.TimestampTz, asOf.AddMinutes(slaHorizonMinutes));
            command.Parameters.AddWithValue("as_of", NpgsqlDbType.TimestampTz, asOf);
            command.Parameters.AddWithValue("day_start", NpgsqlDbType.TimestampTz, dayStart);
            command.Parameters.AddWithValue("day_end", NpgsqlDbType.TimestampTz, dayEnd);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Dashboard aggregate query returned no row.");
            activeMissions = reader.GetInt64(0);
            unassignedMissions = reader.GetInt64(1);
            onlineCouriers = reader.GetInt64(2);
            atRisk = reader.GetInt64(3);
            overdue = reader.GetInt64(4);
            deliveredToday = reader.GetInt64(5);
            onTimeDeliveredToday = reader.GetInt64(6);
        }

        await readAudit.RecordAsync(PostgresMissionOperationsReadAuditService.DashboardSummary,
            null, 1, cancellationToken);

        decimal? onTimeRate = deliveredToday == 0
            ? null
            : Math.Round(onTimeDeliveredToday * 100m / deliveredToday, 1, MidpointRounding.AwayFromZero);
        return new OperationsDashboardSummary(
            asOf, businessDate, TehranTimeZoneId, slaHorizonMinutes,
            activeMissions, unassignedMissions, onlineCouriers, atRisk, overdue,
            deliveredToday, onTimeDeliveredToday, onTimeRate);
    }
}
