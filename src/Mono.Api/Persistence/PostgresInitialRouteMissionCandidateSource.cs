using System.Text.Json;
using Mono.Api.Domain;
using Mono.Api.Services;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>Loads only a persisted PROMISED Mission with its immutable committed Promise version.</summary>
public sealed class PostgresInitialRouteMissionCandidateSource(NpgsqlDataSource dataSource)
    : IInitialRouteMissionCandidateSource
{
    public async Task<InitialRouteMissionCandidate?> FindConfirmedAsync(
        string missionId,
        int pickupServiceSeconds,
        int dropoffServiceSeconds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(missionId) || missionId != missionId.Trim() || missionId.Length > 128 ||
            pickupServiceSeconds is < 0 or > RouteInsertionTimeWindowPlanner.MaximumPickupDwellSeconds ||
            dropoffServiceSeconds < 0)
            throw new ArgumentException("Initial route Mission lookup is malformed.", nameof(missionId));

        const string sql = """
            SELECT m.mission_id, d.delivery_request_id, m.promise_id, m.promise_version,
                   m.committed_delivery_at, m.created_at, d.request_payload
            FROM missions m
            JOIN delivery_requests d
              ON d.customer_id = m.customer_id AND d.delivery_request_id = m.delivery_request_id
            WHERE m.mission_id = @mission_id AND m.status = 'PROMISED';
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        var storedMissionId = reader.GetString(0);
        var deliveryRequestId = reader.GetString(1);
        var promiseId = reader.GetString(2);
        var promiseVersion = reader.GetInt32(3);
        var committedAt = reader.GetFieldValue<DateTimeOffset>(4).ToUniversalTime();
        var missionCreatedAt = reader.GetFieldValue<DateTimeOffset>(5).ToUniversalTime();
        var pickupReadyAt = ResolvePickupReadyAt(reader.GetString(6), missionCreatedAt);
        if (pickupReadyAt is null || !DeliveryRequestInputValidator.IsValidDeliveryRequestId(deliveryRequestId))
            return null;

        var suffix = deliveryRequestId[4..];
        var candidate = new RouteInsertionMission(
            storedMissionId,
            $"loc_{suffix}_pickup",
            $"loc_{suffix}_dropoff",
            pickupReadyAt.Value,
            committedAt,
            pickupServiceSeconds,
            dropoffServiceSeconds);
        return new InitialRouteMissionCandidate(
            storedMissionId, deliveryRequestId, promiseId, promiseVersion, candidate);
    }

    private static DateTimeOffset? ResolvePickupReadyAt(string requestPayload, DateTimeOffset fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(requestPayload);
            if (!document.RootElement.TryGetProperty("estimated_ready_at", out var value) ||
                value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return fallback;
            if (value.ValueKind != JsonValueKind.String ||
                !value.TryGetDateTimeOffset(out var readyAt) || readyAt == default)
                return null;
            return readyAt.ToUniversalTime();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
