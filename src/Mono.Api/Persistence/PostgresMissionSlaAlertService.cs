using System.Text.RegularExpressions;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed record MissionSlaAlertCursor(DateTimeOffset DueAt, string MissionId);
public sealed record MissionSlaAlert(
    string MissionId, string CustomerId, string Status, DateTimeOffset CommittedDeliveryAt,
    string? CourierId, bool IsOverdue);
public sealed record MissionSlaAlertPage(
    IReadOnlyList<MissionSlaAlert> Items, MissionSlaAlertCursor? NextCursor, DateTimeOffset EvaluatedAt);

/// <summary>Time-consistent delivery SLA alerts across all active Missions.</summary>
public sealed class PostgresMissionSlaAlertService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    PostgresMissionOperationsReadAuditService readAudit,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private static readonly Regex CustomerIdPattern = new(@"\Acus_[0-9a-f]{32}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MissionIdPattern = new(@"\Amis_[0-9a-f]{32}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<MissionSlaAlertPage> SearchAsync(
        int pageSize, string? customerId, int minutesAhead, DateTimeOffset? evaluatedAt,
        MissionSlaAlertCursor? before, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        if (pageSize is < 1 or > 100 || minutesAhead is < 0 or > 10080)
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        customerId = string.IsNullOrWhiteSpace(customerId) ? null : customerId.Trim();
        if (customerId is not null && !CustomerIdPattern.IsMatch(customerId))
            throw new ArgumentException("Customer ID is invalid.", nameof(customerId));
        if (before is not null && !MissionIdPattern.IsMatch(before.MissionId))
            throw new ArgumentException("SLA alert cursor is invalid.", nameof(before));

        var asOf = evaluatedAt?.ToUniversalTime() ?? _timeProvider.GetUtcNow();
        var dueThrough = asOf.AddMinutes(minutesAhead);
        const string firstSql = """
            SELECT m.mission_id, m.customer_id, m.status, m.committed_delivery_at,
                   accepted.courier_id, (m.committed_delivery_at <= @as_of) AS is_overdue
            FROM missions m
            LEFT JOIN LATERAL (
                SELECT offer.courier_id
                FROM mission_courier_offers offer
                WHERE offer.mission_id = m.mission_id AND offer.status = 'ACCEPTED'
                ORDER BY offer.created_at DESC, offer.offer_id DESC
                LIMIT 1
            ) accepted ON TRUE
            WHERE m.status IN ('PROMISED', 'SEARCHING_COURIER', 'COURIER_ASSIGNED', 'ARRIVED_PICKUP',
                'HANDED_TO_COURIER', 'PICKED_UP', 'IN_TRANSIT', 'ARRIVED_DROPOFF')
              AND m.committed_delivery_at <= @due_through
              AND (@customer_id IS NULL OR m.customer_id = @customer_id)
            ORDER BY m.committed_delivery_at ASC, m.mission_id ASC
            LIMIT @take;
            """;
        const string nextSql = """
            SELECT m.mission_id, m.customer_id, m.status, m.committed_delivery_at,
                   accepted.courier_id, (m.committed_delivery_at <= @as_of) AS is_overdue
            FROM missions m
            LEFT JOIN LATERAL (
                SELECT offer.courier_id
                FROM mission_courier_offers offer
                WHERE offer.mission_id = m.mission_id AND offer.status = 'ACCEPTED'
                ORDER BY offer.created_at DESC, offer.offer_id DESC
                LIMIT 1
            ) accepted ON TRUE
            WHERE m.status IN ('PROMISED', 'SEARCHING_COURIER', 'COURIER_ASSIGNED', 'ARRIVED_PICKUP',
                'HANDED_TO_COURIER', 'PICKED_UP', 'IN_TRANSIT', 'ARRIVED_DROPOFF')
              AND m.committed_delivery_at <= @due_through
              AND (@customer_id IS NULL OR m.customer_id = @customer_id)
              AND (m.committed_delivery_at > @cursor_due_at
                   OR (m.committed_delivery_at = @cursor_due_at AND m.mission_id > @cursor_mission_id))
            ORDER BY m.committed_delivery_at ASC, m.mission_id ASC
            LIMIT @take;
            """;
        var items = new List<MissionSlaAlert>(pageSize + 1);
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(before is null ? firstSql : nextSql, connection))
        {
            command.Parameters.Add("as_of", NpgsqlDbType.TimestampTz).Value = asOf;
            command.Parameters.Add("due_through", NpgsqlDbType.TimestampTz).Value = dueThrough;
            command.Parameters.Add("customer_id", NpgsqlDbType.Text).Value = customerId is null ? DBNull.Value : customerId;
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);
            if (before is not null)
            {
                command.Parameters.AddWithValue("cursor_due_at", NpgsqlDbType.TimestampTz, before.DueAt);
                command.Parameters.AddWithValue("cursor_mission_id", NpgsqlDbType.Text, before.MissionId);
            }
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new MissionSlaAlert(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetBoolean(5)));
        }
        MissionSlaAlertCursor? next = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            next = new MissionSlaAlertCursor(items[^1].CommittedDeliveryAt, items[^1].MissionId);
        }
        await readAudit.RecordAsync(PostgresMissionOperationsReadAuditService.MissionSlaAlertSearch,
            null, items.Count, cancellationToken);
        return new MissionSlaAlertPage(items, next, asOf);
    }
}
