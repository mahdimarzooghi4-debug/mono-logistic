using Mono.Api.Domain;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Reads a mission's courier dispatch attempts and each attempt's latest immutable event.</summary>
public sealed class PostgresMissionDispatchAttemptHistoryService(NpgsqlDataSource dataSource)
{
    public async Task<MissionDispatchAttemptHistory?> FindAsync(
        string missionId, CancellationToken cancellationToken = default)
    {
        if (!MissionHistoryInputValidator.IsValidMissionId(missionId))
            throw new ArgumentException("Mission ID is invalid.", nameof(missionId));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var exists = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM missions WHERE mission_id = @mission_id);", connection))
        {
            exists.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            if (await exists.ExecuteScalarAsync(cancellationToken) is not true)
                return null;
        }

        const string sql = """
            SELECT offer.offer_id, offer.courier_id, offer.zone_id, offer.status,
                   offer.created_at, offer.updated_at, offer.expires_at,
                   latest.action, latest.actor_type, latest.reason, latest.occurred_at
            FROM mission_courier_offers offer
            LEFT JOIN LATERAL (
                SELECT event.action, event.actor_type, event.reason, event.occurred_at
                FROM mission_courier_offer_events event
                WHERE event.offer_id = offer.offer_id
                ORDER BY event.event_sequence DESC
                LIMIT 1
            ) latest ON TRUE
            WHERE offer.mission_id = @mission_id
            ORDER BY offer.created_at DESC, offer.offer_id DESC
            LIMIT 100;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
        var attempts = new List<MissionDispatchAttempt>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                attempts.Add(new MissionDispatchAttempt(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetFieldValue<DateTimeOffset>(4), reader.GetFieldValue<DateTimeOffset>(5),
                    reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetFieldValue<DateTimeOffset>(10)));
            }
        }
        return new MissionDispatchAttemptHistory(missionId, attempts);
    }
}
