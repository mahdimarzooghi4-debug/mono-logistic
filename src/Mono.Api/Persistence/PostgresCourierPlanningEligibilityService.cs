using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>
/// Explains which couriers contribute to staff-planned zone capacity. Results are planning data,
/// not live availability, assignment eligibility, or dispatch authorization.
/// </summary>
public sealed class PostgresCourierPlanningEligibilityService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    PostgresCourierOperationsReadAuditService readAudit)
{
    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<CourierPlanningEligibilityPage?> SearchAsync(
        string zoneId,
        DateTimeOffset from,
        DateTimeOffset to,
        int pageSize,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        zoneId = zoneId?.Trim() ?? string.Empty;
        if (zoneId.Length is < 1 or > 128 || zoneId.Any(char.IsControl) ||
            from.Offset != TimeSpan.Zero || to.Offset != TimeSpan.Zero || to <= from ||
            to - from > TimeSpan.FromHours(24) || pageSize is < 1 or > 100 ||
            (cursor is not null && !CourierIdPattern.IsMatch(cursor)))
            throw new ArgumentException("Courier planning eligibility filters are invalid.");

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string zoneSql = """
            SELECT EXISTS (SELECT 1 FROM zone_geometry_active WHERE zone_id = @zone_id);
            """;
        await using (var zoneCommand = new NpgsqlCommand(zoneSql, connection))
        {
            zoneCommand.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            if (!(bool)(await zoneCommand.ExecuteScalarAsync(cancellationToken) ?? false))
                throw new ArgumentException("The requested zone is not active.", nameof(zoneId));
        }

        const string candidatesSql = """
            WITH candidates AS (
                SELECT courier.courier_id, courier.mode, courier.status AS trust_status,
                    courier.approved_zone_ids ? @zone_id AS zone_approved,
                    COALESCE(latest.action = 'SUSPENDED', FALSE) AS suspended,
                    COALESCE(presence.is_online, FALSE) AS is_online,
                    presence.zone_id AS live_presence_zone_id,
                    presence.changed_at AS live_presence_updated_at,
                    EXISTS (
                        SELECT 1 FROM courier_availability_windows availability
                        WHERE availability.courier_id = courier.courier_id
                          AND availability.zone_id = @zone_id
                          AND availability.status = 'SCHEDULED'
                          AND availability.starts_at < @to
                          AND availability.ends_at > @from
                    ) AS has_scheduled_window
                FROM couriers courier
                LEFT JOIN courier_live_presence presence ON presence.courier_id = courier.courier_id
                LEFT JOIN LATERAL (
                    SELECT action FROM courier_operational_status_events
                    WHERE courier_id = courier.courier_id
                    ORDER BY event_sequence DESC LIMIT 1
                ) latest ON TRUE
            )
            SELECT courier_id, mode, trust_status,
                CASE WHEN trust_status <> 'ACTIVE' THEN 'NOT_ACTIVE'
                     WHEN suspended THEN 'SUSPENDED' ELSE 'ACTIVE' END AS operational_status,
                zone_approved, has_scheduled_window,
                (trust_status = 'ACTIVE' AND NOT suspended AND zone_approved AND has_scheduled_window)
                    AS planned_capacity_contributor,
                array_remove(ARRAY[
                    CASE WHEN trust_status <> 'ACTIVE' THEN 'COURIER_NOT_ACTIVE' END,
                    CASE WHEN suspended THEN 'COURIER_SUSPENDED' END,
                    CASE WHEN NOT zone_approved THEN 'ZONE_NOT_APPROVED' END,
                    CASE WHEN NOT has_scheduled_window THEN 'NO_SCHEDULED_WINDOW' END
                ]::text[], NULL::text) AS exclusion_reasons,
                is_online, live_presence_zone_id, live_presence_updated_at,
                (trust_status = 'ACTIVE' AND NOT suspended AND zone_approved AND
                    is_online AND live_presence_zone_id = @zone_id) AS live_presence_candidate,
                array_remove(ARRAY[
                    CASE WHEN trust_status <> 'ACTIVE' THEN 'COURIER_NOT_ACTIVE' END,
                    CASE WHEN suspended THEN 'COURIER_SUSPENDED' END,
                    CASE WHEN NOT zone_approved THEN 'ZONE_NOT_APPROVED' END,
                    CASE WHEN NOT is_online THEN 'COURIER_NOT_ONLINE' END,
                    CASE WHEN is_online AND live_presence_zone_id <> @zone_id
                        THEN 'ONLINE_IN_DIFFERENT_ZONE' END
                ]::text[], NULL::text) AS live_presence_exclusion_reasons
            FROM candidates
            WHERE @cursor IS NULL OR courier_id > @cursor
            ORDER BY courier_id
            LIMIT @take;
            """;

        if (cursor is not null)
        {
            const string cursorSql = """
                WITH candidates AS (
                    SELECT courier.courier_id
                    FROM couriers courier
                    WHERE courier.courier_id = @cursor
                )
                SELECT EXISTS (SELECT 1 FROM candidates);
                """;
            await using var cursorCommand = new NpgsqlCommand(cursorSql, connection);
            cursorCommand.Parameters.AddWithValue("cursor", NpgsqlDbType.Text, cursor);
            if (!(bool)(await cursorCommand.ExecuteScalarAsync(cancellationToken) ?? false))
                return null;
        }

        await using var command = new NpgsqlCommand(candidatesSql, connection);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        command.Parameters.AddWithValue("from", NpgsqlDbType.TimestampTz, from);
        command.Parameters.AddWithValue("to", NpgsqlDbType.TimestampTz, to);
        command.Parameters.AddWithValue("cursor", NpgsqlDbType.Text, (object?)cursor ?? DBNull.Value);
        command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, pageSize + 1);

        var items = new List<CourierPlanningEligibilityItem>(pageSize + 1);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new CourierPlanningEligibilityItem(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetBoolean(4),
                    reader.GetBoolean(5),
                    reader.GetBoolean(6),
                    reader.GetFieldValue<string[]>(7),
                    reader.GetBoolean(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10),
                    reader.GetBoolean(11),
                    reader.GetFieldValue<string[]>(12)));
        }

        string? nextCursor = null;
        if (items.Count > pageSize)
        {
            items.RemoveAt(items.Count - 1);
            nextCursor = items[^1].CourierId;
        }

        await readAudit.RecordAsync(
            PostgresCourierOperationsReadAuditService.CourierPlanningEligibilitySearch,
            null, items.Count, cancellationToken);
        return new CourierPlanningEligibilityPage(
            zoneId, from, to, "PLANNED_CAPACITY_AND_LIVE_PRESENCE_SIGNALS", items, nextCursor);
    }

    private void RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
    }
}
