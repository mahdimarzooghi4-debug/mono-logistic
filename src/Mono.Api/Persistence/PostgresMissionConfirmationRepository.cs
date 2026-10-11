using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed class PostgresMissionConfirmationRepository(
    NpgsqlDataSource dataSource,
    ICustomerContext customerContext,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<MissionConfirmationResult> ConfirmAsync(
        string deliveryRequestId,
        string quoteId,
        int promiseVersion,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(quoteId) || quoteId.Length > 128 || promiseVersion < 1)
            throw new ArgumentException("A quote ID and positive Promise version are required.");

        var customerId = customerContext.CustomerId;
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { delivery_request_id = deliveryRequestId, quote_id = quoteId, promise_version = promiseVersion }))))
            .ToLowerInvariant();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        const string lockRequestSql = """
            SELECT 1 FROM delivery_requests
            WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id
            FOR UPDATE;
            """;
        await using (var lockRequest = new NpgsqlCommand(lockRequestSql, connection, transaction))
        {
            lockRequest.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            lockRequest.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
            if (await lockRequest.ExecuteScalarAsync(cancellationToken) is null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new MissionConfirmationResult(MissionConfirmationOutcome.DeliveryRequestNotFound, null);
            }
        }

        const string replaySql = """
            SELECT i.request_hash, m.mission_id, m.delivery_request_id, m.quote_id, m.quote_version,
                   m.promise_id, m.promise_version, m.status, m.committed_delivery_at, m.created_at
            FROM mission_confirmation_idempotency i
            JOIN missions m
              ON m.customer_id = i.customer_id AND m.delivery_request_id = i.delivery_request_id
            WHERE i.customer_id = @customer_id AND i.idempotency_key = @idempotency_key;
            """;
        await using (var replay = new NpgsqlCommand(replaySql, connection, transaction))
        {
            replay.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            replay.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            await using var reader = await replay.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var sameRequest = string.Equals(reader.GetString(0), requestHash, StringComparison.Ordinal);
                var response = ReadMission(reader, 1);
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                return sameRequest
                    ? new MissionConfirmationResult(MissionConfirmationOutcome.Replayed, response)
                    : new MissionConfirmationResult(MissionConfirmationOutcome.Conflict, null);
            }
        }

        const string quoteSql = """
            SELECT version, expires_at
            FROM delivery_quote_versions
            WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id
              AND quote_id = @quote_id
            ORDER BY version DESC
            LIMIT 1;
            """;
        int quoteVersion;
        await using (var quote = new NpgsqlCommand(quoteSql, connection, transaction))
        {
            quote.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            quote.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
            quote.Parameters.AddWithValue("quote_id", NpgsqlDbType.Text, quoteId);
            await using var reader = await quote.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                return new MissionConfirmationResult(MissionConfirmationOutcome.QuoteNotFound, null);
            }

            quoteVersion = reader.GetInt32(0);
            var expiresAt = reader.GetFieldValue<DateTimeOffset>(1);
            await reader.DisposeAsync();
            if (expiresAt <= _timeProvider.GetUtcNow())
            {
                await transaction.CommitAsync(cancellationToken);
                return new MissionConfirmationResult(MissionConfirmationOutcome.QuoteExpired, null);
            }
        }

        const string promiseSql = """
            SELECT p.promise_id, p.current_version, p.committed_delivery_at, pv.version
            FROM promises p
            JOIN promise_versions pv ON pv.promise_id = p.promise_id AND pv.version = p.current_version
            WHERE p.customer_id = @customer_id AND p.delivery_request_id = @delivery_request_id
              AND p.current_version = @promise_version;
            """;
        string? promiseId = null;
        DateTimeOffset committedDeliveryAt = default;
        await using (var promise = new NpgsqlCommand(promiseSql, connection, transaction))
        {
            promise.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            promise.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
            promise.Parameters.AddWithValue("promise_version", NpgsqlDbType.Integer, promiseVersion);
            await using var reader = await promise.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                promiseId = reader.GetString(0);
                committedDeliveryAt = reader.GetFieldValue<DateTimeOffset>(2);
            }
        }

        if (promiseId is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionConfirmationResult(MissionConfirmationOutcome.PromiseNotFound, null);
        }

        const string existingMissionSql = """
            SELECT mission_id FROM missions
            WHERE customer_id = @customer_id AND delivery_request_id = @delivery_request_id;
            """;
        await using (var existing = new NpgsqlCommand(existingMissionSql, connection, transaction))
        {
            existing.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            existing.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
            if (await existing.ExecuteScalarAsync(cancellationToken) is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new MissionConfirmationResult(MissionConfirmationOutcome.AlreadyConfirmed, null);
            }
        }

        var missionId = $"mis_{Guid.NewGuid():N}";
        var eventId = $"evt_{Guid.NewGuid():N}";
        var occurredAt = _timeProvider.GetUtcNow();
        var eventData = JsonSerializer.Serialize(new
        {
            mission_id = missionId,
            delivery_request_id = deliveryRequestId,
            quote_id = quoteId,
            quote_version = 1,
            promise_id = promiseId,
            promise_version = promiseVersion,
            status = "PROMISED",
            committed_delivery_at = committedDeliveryAt
        });

        const string insertMissionSql = """
            INSERT INTO missions (
                mission_id, customer_id, delivery_request_id, quote_id, quote_version,
                promise_id, promise_version, status, committed_delivery_at, created_at)
            VALUES (
                @mission_id, @customer_id, @delivery_request_id, @quote_id, @quote_version,
                @promise_id, @promise_version, 'PROMISED', @committed_delivery_at, @created_at);
            """;
        await using (var insertMission = new NpgsqlCommand(insertMissionSql, connection, transaction))
        {
            insertMission.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            insertMission.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            insertMission.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
            insertMission.Parameters.AddWithValue("quote_id", NpgsqlDbType.Text, quoteId);
            insertMission.Parameters.AddWithValue("quote_version", NpgsqlDbType.Integer, quoteVersion);
            insertMission.Parameters.AddWithValue("promise_id", NpgsqlDbType.Text, promiseId);
            insertMission.Parameters.AddWithValue("promise_version", NpgsqlDbType.Integer, promiseVersion);
            insertMission.Parameters.AddWithValue("committed_delivery_at", NpgsqlDbType.TimestampTz, committedDeliveryAt);
            insertMission.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, occurredAt);
            await insertMission.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertIdempotencySql = """
            INSERT INTO mission_confirmation_idempotency (
                customer_id, idempotency_key, delivery_request_id, request_hash, mission_id, created_at)
            VALUES (@customer_id, @idempotency_key, @delivery_request_id, @request_hash, @mission_id, @created_at);
            """;
        await using (var insertKey = new NpgsqlCommand(insertIdempotencySql, connection, transaction))
        {
            insertKey.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            insertKey.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            insertKey.Parameters.AddWithValue("delivery_request_id", NpgsqlDbType.Text, deliveryRequestId);
            insertKey.Parameters.AddWithValue("request_hash", NpgsqlDbType.Text, requestHash);
            insertKey.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            insertKey.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, occurredAt);
            await insertKey.ExecuteNonQueryAsync(cancellationToken);
        }

        const string outboxSql = """
            INSERT INTO outbox_events (
                event_id, event_type, event_version, occurred_at, producer, correlation_id,
                causation_id, customer_id, aggregate_type, aggregate_id, sequence, data, created_at)
            VALUES (
                @event_id, 'mission.created', 1, @occurred_at, 'mono-api', @correlation_id,
                @causation_id, @customer_id, 'MISSION', @aggregate_id, 1, @data, @created_at);
            """;
        await using (var outbox = new NpgsqlCommand(outboxSql, connection, transaction))
        {
            outbox.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            outbox.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
            outbox.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Text, deliveryRequestId);
            outbox.Parameters.AddWithValue("causation_id", NpgsqlDbType.Text, $"confirm:{idempotencyKey}");
            outbox.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            outbox.Parameters.AddWithValue("aggregate_id", NpgsqlDbType.Text, missionId);
            outbox.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, eventData);
            outbox.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, occurredAt);
            await outbox.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new MissionConfirmationResult(
            MissionConfirmationOutcome.Created,
            new MissionConfirmationResponse(
                missionId, deliveryRequestId, quoteId, quoteVersion, promiseId, promiseVersion,
                "PROMISED", committedDeliveryAt, occurredAt));
    }

    public async Task<MissionTimelineResponse?> FindTimelineAsync(
        string missionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT m.mission_id, e.event_id, e.event_type, e.sequence, e.occurred_at
            FROM missions m
            LEFT JOIN outbox_events e
              ON e.customer_id = m.customer_id
             AND e.aggregate_type = 'mission'
             AND e.aggregate_id = m.mission_id
             AND e.event_type IN (
                 'mission.created', 'mission.promised', 'mission.searching_courier',
                 'mission.courier_assigned', 'mission.arrived_pickup', 'mission.handed_to_courier',
                 'mission.picked_up', 'mission.in_transit', 'mission.arrived_dropoff',
                 'mission.delivered', 'mission.failed', 'mission.cancelled')
            WHERE m.customer_id = @customer_id AND m.mission_id = @mission_id
            ORDER BY e.sequence ASC;
            """;

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerContext.CustomerId);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        string? foundMissionId = null;
        var events = new List<MissionTimelineEvent>();
        while (await reader.ReadAsync(cancellationToken))
        {
            foundMissionId ??= reader.GetString(0);
            if (reader.IsDBNull(1))
                continue;
            events.Add(new MissionTimelineEvent(
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt64(3),
                reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return foundMissionId is null ? null : new MissionTimelineResponse(foundMissionId, events);
    }

    public async Task<MissionConfirmationResponse?> FindByIdAsync(
        string missionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT mission_id, delivery_request_id, quote_id, quote_version,
                   promise_id, promise_version, status, committed_delivery_at, created_at
            FROM missions
            WHERE customer_id = @customer_id AND mission_id = @mission_id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerContext.CustomerId);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMission(reader, 0) : null;
    }

    private static MissionConfirmationResponse ReadMission(NpgsqlDataReader reader, int offset) =>
        new(
            reader.GetString(offset),
            reader.GetString(offset + 1),
            reader.GetString(offset + 2),
            reader.GetInt32(offset + 3),
            reader.GetString(offset + 4),
            reader.GetInt32(offset + 5),
            reader.GetString(offset + 6),
            reader.GetFieldValue<DateTimeOffset>(offset + 7),
            reader.GetFieldValue<DateTimeOffset>(offset + 8));
}
