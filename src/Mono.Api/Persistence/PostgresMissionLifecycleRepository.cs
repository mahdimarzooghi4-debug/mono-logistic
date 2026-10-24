using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public sealed class PostgresMissionLifecycleRepository(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    ICustomerContext? customerContext = null,
    TimeProvider? timeProvider = null)
{
    private readonly ICustomerContext? _customerContext = customerContext;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public Task<MissionLifecycleResult> ApplyAsync(
        string missionId,
        string eventType,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        ApplyCoreAsync(missionId, eventType, reason, idempotencyKey, cancellationToken,
            customerScopeId: null, actorType: "operations", externalReference: null, courierScopeId: null);

    public Task<MissionLifecycleResult> ApplyForCourierAsync(
        string courierId, string missionId, string eventType, string reason, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(courierId) || courierId.Length != 36 ||
            !courierId.StartsWith("crr_", StringComparison.Ordinal) ||
            courierId[4..].Any(character => !Uri.IsHexDigit(character) || char.IsUpper(character)))
            throw new ArgumentException("Courier identity is invalid.", nameof(courierId));
        if (eventType is not ("mission.arrived_pickup" or "mission.handed_to_courier" or
            "mission.picked_up" or "mission.in_transit" or "mission.arrived_dropoff" or "mission.delivered"))
            throw new ArgumentException("Courier mission event type is invalid.", nameof(eventType));
        return ApplyCoreAsync(missionId, eventType, reason, idempotencyKey, cancellationToken,
            customerScopeId: null, actorType: "courier", externalReference: null, courierScopeId: courierId);
    }

    private async Task<MissionLifecycleResult> ApplyCoreAsync(
        string missionId,
        string eventType,
        string reason,
        string idempotencyKey,
        CancellationToken cancellationToken,
        string? customerScopeId,
        string actorType,
        string? externalReference,
        string? courierScopeId)
    {
        missionId = Required(missionId, nameof(missionId), 128);
        eventType = Required(eventType, nameof(eventType), 64);
        reason = Required(reason, nameof(reason), 500);
        idempotencyKey = Required(idempotencyKey, nameof(idempotencyKey), 128);
        if (idempotencyKey.Any(char.IsControl))
            throw new ArgumentException("Idempotency key is invalid.", nameof(idempotencyKey));
        if (!MissionLifecycleTransitions.EventTypes.Contains(eventType, StringComparer.Ordinal))
            throw new ArgumentException("Mission event type is invalid.", nameof(eventType));

        var actorId = actorType switch
        {
            "customer" => Required(customerScopeId, nameof(customerScopeId), 128),
            "courier" => Required(courierScopeId, nameof(courierScopeId), 128),
            _ => Required(operationsContext.ActorId, nameof(operationsContext.ActorId), 128)
        };
        var requestJson = actorType == "operations" && externalReference is null
            ? JsonSerializer.Serialize(new { mission_id = missionId, event_type = eventType, reason })
            : JsonSerializer.Serialize(new { mission_id = missionId, event_type = eventType, reason, external_reference = externalReference, actor_type = actorType });
        var requestHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(requestJson)))
            .ToLowerInvariant();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var lockMissionSql = courierScopeId is null ? """
            SELECT customer_id, delivery_request_id, status
            FROM missions
            WHERE mission_id = @mission_id
              AND (@customer_scope_id IS NULL OR customer_id = @customer_scope_id)
            FOR UPDATE;
            """ : """
            SELECT customer_id, delivery_request_id, status
            FROM missions
            WHERE mission_id = @mission_id
              AND (@customer_scope_id IS NULL OR customer_id = @customer_scope_id)
              AND EXISTS (
                  SELECT 1 FROM mission_courier_offers offer
                  WHERE offer.mission_id = missions.mission_id
                    AND offer.courier_id = @courier_scope_id AND offer.status = 'ACCEPTED')
            FOR UPDATE;
            """;
        string? customerId = null;
        string? deliveryRequestId = null;
        string? currentStatus = null;
        await using (var lockMission = new NpgsqlCommand(lockMissionSql, connection, transaction))
        {
            lockMission.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            lockMission.Parameters.AddWithValue("customer_scope_id", NpgsqlDbType.Text,
                (object?)customerScopeId ?? DBNull.Value);
            if (courierScopeId is not null)
                lockMission.Parameters.AddWithValue("courier_scope_id", NpgsqlDbType.Text, courierScopeId);
            await using var reader = await lockMission.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                customerId = reader.GetString(0);
                deliveryRequestId = reader.GetString(1);
                currentStatus = reader.GetString(2);
            }
        }

        if (customerId is null || deliveryRequestId is null || currentStatus is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionLifecycleResult(MissionLifecycleOutcome.MissionNotFound, null);
        }

        const string replaySql = """
            SELECT request_hash, event_id, event_type, sequence, result_status, occurred_at
            FROM mission_lifecycle_idempotency
            WHERE customer_id = @customer_id AND mission_id = @mission_id
              AND idempotency_key = @idempotency_key;
            """;
        await using (var replay = new NpgsqlCommand(replaySql, connection, transaction))
        {
            replay.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            replay.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            replay.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            await using var reader = await replay.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var sameRequest = string.Equals(reader.GetString(0), requestHash, StringComparison.Ordinal);
                var response = new MissionLifecycleResponse(
                    missionId, reader.GetString(1), reader.GetString(2), reader.GetInt64(3),
                    reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5));
                await reader.DisposeAsync();
                await transaction.CommitAsync(cancellationToken);
                return sameRequest
                    ? new MissionLifecycleResult(MissionLifecycleOutcome.Replayed, response)
                    : new MissionLifecycleResult(MissionLifecycleOutcome.Conflict, null);
            }
        }

        if (!MissionLifecycleTransitions.TryGetNextStatus(currentStatus, eventType, out var nextStatus))
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionLifecycleResult(MissionLifecycleOutcome.InvalidTransition, null, currentStatus);
        }

        const string nextSequenceSql = """
            SELECT COALESCE(MAX(sequence), 0) + 1
            FROM outbox_events
            WHERE customer_id = @customer_id
              AND aggregate_type = 'mission'
              AND aggregate_id = @mission_id;
            """;
        long sequence;
        await using (var nextSequence = new NpgsqlCommand(nextSequenceSql, connection, transaction))
        {
            nextSequence.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            nextSequence.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            sequence = (long)(await nextSequence.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Mission event sequence was not returned."));
        }

        var eventId = $"evt_{Guid.NewGuid():N}";
        var now = _timeProvider.GetUtcNow();
        // PostgreSQL timestamp precision is one microsecond; keep the first response identical to a replay.
        var occurredAt = new DateTimeOffset(now.Ticks - now.Ticks % 10, TimeSpan.Zero);
        var eventData = JsonSerializer.Serialize(new
        {
            mission_id = missionId,
            delivery_request_id = deliveryRequestId,
            event_type = eventType,
            status = nextStatus,
            sequence,
            actor_type = actorType,
            actor_id = actorId,
            reason,
            external_reference = externalReference,
            occurred_at = occurredAt
        });

        const string updateMissionSql = """
            UPDATE missions SET status = @status
            WHERE mission_id = @mission_id AND customer_id = @customer_id;
            """;
        await using (var updateMission = new NpgsqlCommand(updateMissionSql, connection, transaction))
        {
            updateMission.Parameters.AddWithValue("status", NpgsqlDbType.Text, nextStatus);
            updateMission.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            updateMission.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            if (await updateMission.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new InvalidOperationException("Locked Mission disappeared during transition.");
        }

        if (eventType == "mission.delivered")
            await PostgresCourierEarningLedger.PostMissionEarningAsync(connection, transaction,
                customerId, missionId, occurredAt, cancellationToken);

        const string insertEventSql = """
            INSERT INTO outbox_events (
                event_id, event_type, event_version, occurred_at, producer, correlation_id,
                causation_id, customer_id, aggregate_type, aggregate_id, sequence, data, created_at)
            VALUES (
                @event_id, @event_type, 1, @occurred_at, 'mono-api', @correlation_id,
                @causation_id, @customer_id, 'mission', @mission_id, @sequence, @data, @created_at);
            """;
        await using (var insertEvent = new NpgsqlCommand(insertEventSql, connection, transaction))
        {
            insertEvent.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            insertEvent.Parameters.AddWithValue("event_type", NpgsqlDbType.Text, eventType);
            insertEvent.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
            insertEvent.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Text, deliveryRequestId);
            insertEvent.Parameters.AddWithValue("causation_id", NpgsqlDbType.Text,
                $"{actorType}:{actorId}:{idempotencyKey}");
            insertEvent.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            insertEvent.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            insertEvent.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, sequence);
            insertEvent.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, eventData);
            insertEvent.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, occurredAt);
            await insertEvent.ExecuteNonQueryAsync(cancellationToken);
        }

        const string idempotencySql = """
            INSERT INTO mission_lifecycle_idempotency (
                customer_id, mission_id, idempotency_key, request_hash, event_id,
                event_type, sequence, result_status, occurred_at, created_at)
            VALUES (
                @customer_id, @mission_id, @idempotency_key, @request_hash, @event_id,
                @event_type, @sequence, @result_status, @occurred_at, @created_at);
            """;
        await using (var insertReplay = new NpgsqlCommand(idempotencySql, connection, transaction))
        {
            insertReplay.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
            insertReplay.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            insertReplay.Parameters.AddWithValue("idempotency_key", NpgsqlDbType.Text, idempotencyKey);
            insertReplay.Parameters.AddWithValue("request_hash", NpgsqlDbType.Text, requestHash);
            insertReplay.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            insertReplay.Parameters.AddWithValue("event_type", NpgsqlDbType.Text, eventType);
            insertReplay.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, sequence);
            insertReplay.Parameters.AddWithValue("result_status", NpgsqlDbType.Text, nextStatus);
            insertReplay.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
            insertReplay.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, occurredAt);
            await insertReplay.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new MissionLifecycleResult(
            MissionLifecycleOutcome.Applied,
            new MissionLifecycleResponse(missionId, eventId, eventType, sequence, nextStatus, occurredAt));
    }

    public Task<MissionLifecycleResult> CancelForCustomerAsync(
        string missionId,
        string reasonCode,
        string? externalReference,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(reasonCode, "CUSTOMER_CANCELLED", StringComparison.Ordinal))
            throw new ArgumentException("Mission cancellation reason code is invalid.", nameof(reasonCode));

        var customerId = _customerContext?.CustomerId
            ?? throw new InvalidOperationException("Customer context is required for customer Mission cancellation.");
        if (externalReference is not null)
        {
            externalReference = externalReference.Trim();
            if (externalReference.Length is < 1 or > 128 || externalReference.Any(char.IsControl))
                throw new ArgumentException("External reference is invalid.", nameof(externalReference));
        }

        return ApplyCoreAsync(
            missionId,
            "mission.cancelled",
            reasonCode,
            idempotencyKey,
            cancellationToken,
            customerId,
            "customer",
            externalReference,
            courierScopeId: null);
    }

    private static string Required(string? value, string parameterName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);
        var trimmed = value.Trim();
        if (trimmed.Length > maximumLength)
            throw new ArgumentException("Value is too long.", parameterName);
        return trimmed;
    }
}
