using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Api.Domain;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>
/// Coordinates a single operations-created offer and the courier's explicit response.
/// Accepting the offer and assigning its Mission commit in one PostgreSQL transaction.
/// </summary>
public sealed class PostgresMissionCourierOfferService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    TimeProvider? timeProvider = null)
{
    private static readonly Regex MissionIdPattern = new(
        @"\Amis_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex OfferIdPattern = new(
        @"\Amco_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ZoneIdPattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._~-]{0,127}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex KeyPattern = new(
        @"\A[A-Za-z0-9._~-]{1,128}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<MissionCourierOfferResult> CreateOfferAsync(
        string missionId, string courierId, string zoneId, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        Validate(MissionIdPattern, missionId, nameof(missionId));
        Validate(CourierIdPattern, courierId, nameof(courierId));
        Validate(ZoneIdPattern, zoneId, nameof(zoneId));
        Validate(KeyPattern, idempotencyKey, nameof(idempotencyKey));
        var hash = Hash(JsonSerializer.Serialize(new { mission_id = missionId, courier_id = courierId, zone_id = zoneId }));
        var now = UtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockKeyAsync(connection, transaction, missionId, "CREATE_OFFER", idempotencyKey, cancellationToken);
        var replay = await FindReplayAsync(
            connection, transaction, missionId, "CREATE_OFFER", idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var mission = await LockMissionAsync(connection, transaction, missionId, cancellationToken);
        if (mission is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierOfferResult(MissionCourierOfferOutcome.MissionNotFound, null);
        }
        if (mission.Value.Status != "SEARCHING_COURIER")
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierOfferResult(
                MissionCourierOfferOutcome.InvalidState, null, mission.Value.Status);
        }

        if (await HasActiveOfferAsync(connection, transaction, missionId, courierId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierOfferResult(MissionCourierOfferOutcome.InvalidState, null, mission.Value.Status);
        }

        var availability = await CheckCourierAsync(
            connection, transaction, courierId, zoneId, cancellationToken);
        if (!availability.Exists)
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierOfferResult(MissionCourierOfferOutcome.CourierNotFound, null);
        }
        if (!availability.Available)
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierOfferResult(MissionCourierOfferOutcome.CourierNotAvailable, null);
        }

        var offerId = "mco_" + Guid.NewGuid().ToString("N");
        var eventId = "mce_" + Guid.NewGuid().ToString("N");
        const string insertEventSql = """
            INSERT INTO mission_courier_offer_events
                (event_id, offer_id, mission_id, customer_id, courier_id, zone_id,
                 action, actor_type, actor_id, reason, occurred_at)
            VALUES
                (@event_id, @offer_id, @mission_id, @customer_id, @courier_id, @zone_id,
                 'OFFERED', 'OPERATIONS', @actor_id, 'OPERATIONS_CREATED_OFFER', @occurred_at);
            """;
        await using (var command = new NpgsqlCommand(insertEventSql, connection, transaction))
        {
            command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            command.Parameters.AddWithValue("offer_id", NpgsqlDbType.Text, offerId);
            command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, mission.Value.CustomerId);
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, RequireAdministrator());
            command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var snapshot = await ReadOfferAsync(connection, transaction, offerId, cancellationToken)
            ?? throw new InvalidOperationException("Created courier offer projection is missing.");
        await SaveIdempotencyAsync(connection, transaction, missionId, "CREATE_OFFER",
            idempotencyKey, hash, snapshot, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new MissionCourierOfferResult(MissionCourierOfferOutcome.Applied, snapshot);
    }

    public async Task<IReadOnlyList<MissionCourierOffer>> ListMyOffersAsync(
        string courierId, CancellationToken cancellationToken = default)
    {
        Validate(CourierIdPattern, courierId, nameof(courierId));
        const string sql = """
            SELECT offer_id, mission_id, courier_id, zone_id, status, created_at, updated_at
            FROM mission_courier_offers
            WHERE courier_id = @courier_id AND status = 'OFFERED'
            ORDER BY created_at DESC, offer_id DESC
            LIMIT 1;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        var offers = new List<MissionCourierOffer>(1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            offers.Add(ReadOffer(reader));
        return offers;
    }

    public async Task<IReadOnlyList<MissionCourierOffer>> ListForMissionAsync(
        string missionId, CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        Validate(MissionIdPattern, missionId, nameof(missionId));
        const string sql = """
            SELECT offer_id, mission_id, courier_id, zone_id, status, created_at, updated_at
            FROM mission_courier_offers
            WHERE mission_id = @mission_id
            ORDER BY created_at DESC, offer_id DESC
            LIMIT 100;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
        var offers = new List<MissionCourierOffer>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            offers.Add(ReadOffer(reader));
        return offers;
    }

    public Task<MissionCourierOfferResult> AcceptAsync(
        string offerId, string courierId, string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        RespondAsync(offerId, courierId, idempotencyKey, accept: true, cancellationToken);

    public Task<MissionCourierOfferResult> DeclineAsync(
        string offerId, string courierId, string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        RespondAsync(offerId, courierId, idempotencyKey, accept: false, cancellationToken);

    private async Task<MissionCourierOfferResult> RespondAsync(
        string offerId, string courierId, string idempotencyKey, bool accept,
        CancellationToken cancellationToken)
    {
        Validate(OfferIdPattern, offerId, nameof(offerId));
        Validate(CourierIdPattern, courierId, nameof(courierId));
        Validate(KeyPattern, idempotencyKey, nameof(idempotencyKey));
        var operation = accept ? "ACCEPT_OFFER" : "DECLINE_OFFER";
        var hash = Hash(JsonSerializer.Serialize(new { offer_id = offerId, courier_id = courierId, action = operation }));
        var now = UtcNow();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockKeyAsync(connection, transaction, offerId, operation, idempotencyKey, cancellationToken);
        var replay = await FindReplayAsync(
            connection, transaction, offerId, operation, idempotencyKey, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        var offerMissionId = await FindOwnedOfferMissionAsync(
            connection, transaction, offerId, courierId, cancellationToken);
        if (offerMissionId is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierOfferResult(MissionCourierOfferOutcome.OfferNotFound, null);
        }
        var mission = await LockMissionAsync(connection, transaction, offerMissionId, cancellationToken);
        if (mission is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierOfferResult(MissionCourierOfferOutcome.OfferNotFound, null);
        }
        var offer = await LockOfferAsync(connection, transaction, offerId, courierId, cancellationToken);
        if (offer is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierOfferResult(MissionCourierOfferOutcome.OfferNotFound, null);
        }
        if (offer.Status != "OFFERED")
        {
            await transaction.CommitAsync(cancellationToken);
            return new MissionCourierOfferResult(
                MissionCourierOfferOutcome.InvalidState, offer, mission.Value.Status);
        }

        if (accept)
        {
            if (mission.Value.Status != "SEARCHING_COURIER")
            {
                await transaction.CommitAsync(cancellationToken);
                return new MissionCourierOfferResult(
                    MissionCourierOfferOutcome.InvalidState, offer, mission.Value.Status);
            }
            var availability = await CheckCourierAsync(
                connection, transaction, courierId, offer.ZoneId, cancellationToken);
            if (!availability.Exists)
            {
                await transaction.CommitAsync(cancellationToken);
                return new MissionCourierOfferResult(MissionCourierOfferOutcome.CourierNotFound, offer);
            }
            if (!availability.Available)
            {
                await transaction.CommitAsync(cancellationToken);
                return new MissionCourierOfferResult(MissionCourierOfferOutcome.CourierNotAvailable, offer);
            }
        }

        var eventAction = accept ? "ACCEPTED" : "DECLINED";
        var eventId = "mce_" + Guid.NewGuid().ToString("N");
        const string insertOfferEventSql = """
            INSERT INTO mission_courier_offer_events
                (event_id, offer_id, mission_id, customer_id, courier_id, zone_id,
                 action, actor_type, actor_id, reason, occurred_at)
            VALUES (@event_id, @offer_id, @mission_id, @customer_id, @courier_id, @zone_id,
                @action, 'COURIER', @courier_id, @reason, @occurred_at);
            """;
        await using (var command = new NpgsqlCommand(insertOfferEventSql, connection, transaction))
        {
            command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
            command.Parameters.AddWithValue("offer_id", NpgsqlDbType.Text, offerId);
            command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, offer.MissionId);
            command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, mission.Value.CustomerId);
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, offer.ZoneId);
            command.Parameters.AddWithValue("action", NpgsqlDbType.Text, eventAction);
            command.Parameters.AddWithValue("reason", NpgsqlDbType.Text,
                accept ? "COURIER_ACCEPTED_OFFER" : "COURIER_DECLINED_OFFER");
            command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        if (accept)
        {
            var sequence = await NextMissionSequenceAsync(
                connection, transaction, mission.Value.CustomerId, offer.MissionId, cancellationToken);
            const string updateMissionSql = """
                UPDATE missions SET status = 'COURIER_ASSIGNED'
                WHERE mission_id = @mission_id AND status = 'SEARCHING_COURIER';
                """;
            await using (var command = new NpgsqlCommand(updateMissionSql, connection, transaction))
            {
                command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, offer.MissionId);
                if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
                    throw new InvalidOperationException("Mission status changed while its row was locked.");
            }
            await InsertMissionAssignedEventAsync(connection, transaction, offer, mission.Value,
                courierId, sequence, now, idempotencyKey, cancellationToken);
        }

        var snapshot = await ReadOfferAsync(connection, transaction, offerId, cancellationToken)
            ?? throw new InvalidOperationException("Courier offer disappeared during response.");
        await SaveIdempotencyAsync(connection, transaction, offerId, operation,
            idempotencyKey, hash, snapshot, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new MissionCourierOfferResult(MissionCourierOfferOutcome.Applied, snapshot,
            accept ? "COURIER_ASSIGNED" : mission.Value.Status);
    }

    private async Task InsertMissionAssignedEventAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, MissionCourierOffer offer,
        (string CustomerId, string Status, string DeliveryRequestId) mission,
        string courierId, long sequence, DateTimeOffset now, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var eventId = "evt_" + Guid.NewGuid().ToString("N");
        var payload = JsonSerializer.Serialize(new
        {
            mission_id = offer.MissionId,
            delivery_request_id = mission.DeliveryRequestId,
            event_type = "mission.courier_assigned",
            status = "COURIER_ASSIGNED",
            sequence,
            actor_type = "courier",
            actor_id = courierId,
            courier_id = courierId,
            courier_offer_id = offer.OfferId,
            zone_id = offer.ZoneId,
            reason = "COURIER_ACCEPTED_OFFER",
            occurred_at = now
        });
        const string sql = """
            INSERT INTO outbox_events (
                event_id, event_type, event_version, occurred_at, producer, correlation_id,
                causation_id, customer_id, aggregate_type, aggregate_id, sequence, data, created_at)
            VALUES (
                @event_id, 'mission.courier_assigned', 1, @occurred_at, 'mono-api',
                @correlation_id, @causation_id, @customer_id, 'mission', @mission_id,
                @sequence, @data, @created_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("event_id", NpgsqlDbType.Text, eventId);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, now);
        command.Parameters.AddWithValue("correlation_id", NpgsqlDbType.Text, mission.DeliveryRequestId);
        command.Parameters.AddWithValue("causation_id", NpgsqlDbType.Text, $"courier:{courierId}:{idempotencyKey}");
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, mission.CustomerId);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, offer.MissionId);
        command.Parameters.AddWithValue("sequence", NpgsqlDbType.Bigint, sequence);
        command.Parameters.AddWithValue("data", NpgsqlDbType.Jsonb, payload);
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> NextMissionSequenceAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string customerId, string missionId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(MAX(sequence), 0) + 1 FROM outbox_events
            WHERE customer_id = @customer_id AND aggregate_type = 'mission' AND aggregate_id = @mission_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
        return (long)(await command.ExecuteScalarAsync(cancellationToken)
            ?? throw new InvalidOperationException("Mission event sequence was not returned."));
    }

    private static async Task<(string CustomerId, string Status, string DeliveryRequestId)?> LockMissionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string missionId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT customer_id, status, delivery_request_id
            FROM missions WHERE mission_id = @mission_id FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }

    private static async Task<bool> HasActiveOfferAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string missionId, string courierId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM mission_courier_offers
                WHERE (mission_id = @mission_id OR courier_id = @courier_id)
                  AND status IN ('OFFERED', 'ACCEPTED'));
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("mission_id", NpgsqlDbType.Text, missionId);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<(bool Exists, bool Available)> CheckCourierAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string courierId, string zoneId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT courier.status = 'ACTIVE'
                AND NOT COALESCE(latest.action = 'SUSPENDED', FALSE)
                AND COALESCE(presence.is_online, FALSE)
                AND presence.zone_id = @zone_id
                AND courier.approved_zone_ids ? @zone_id
                AND EXISTS (SELECT 1 FROM zone_geometry_active WHERE zone_id = @zone_id)
            FROM couriers courier
            LEFT JOIN courier_live_presence presence ON presence.courier_id = courier.courier_id
            LEFT JOIN LATERAL (
                SELECT action FROM courier_operational_status_events
                WHERE courier_id = courier.courier_id ORDER BY event_sequence DESC LIMIT 1
            ) latest ON TRUE
            WHERE courier.courier_id = @courier_id FOR UPDATE OF courier;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is null ? (false, false) : (true, result is true);
    }

    private static async Task<string?> FindOwnedOfferMissionAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string offerId, string courierId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT mission_id FROM mission_courier_offers
            WHERE offer_id = @offer_id AND courier_id = @courier_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("offer_id", NpgsqlDbType.Text, offerId);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<MissionCourierOffer?> LockOfferAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string offerId, string courierId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT offer_id, mission_id, courier_id, zone_id, status, created_at, updated_at
            FROM mission_courier_offers
            WHERE offer_id = @offer_id AND courier_id = @courier_id FOR UPDATE;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("offer_id", NpgsqlDbType.Text, offerId);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadOffer(reader) : null;
    }

    private static async Task<MissionCourierOffer?> ReadOfferAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string offerId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT offer_id, mission_id, courier_id, zone_id, status, created_at, updated_at
            FROM mission_courier_offers WHERE offer_id = @offer_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("offer_id", NpgsqlDbType.Text, offerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadOffer(reader) : null;
    }

    private static MissionCourierOffer ReadOffer(NpgsqlDataReader reader) =>
        new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6));

    private static async Task LockKeyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string scope, string operation, string key, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", connection, transaction);
        command.Parameters.AddWithValue("lock_key", NpgsqlDbType.Text, $"{scope}:{operation}:{key}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<MissionCourierOfferResult?> FindReplayAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string scope, string operation, string key, string hash, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT request_hash, response FROM mission_courier_offer_idempotency
            WHERE scope_id = @scope_id AND operation = @operation AND idempotency_key = @key;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("scope_id", NpgsqlDbType.Text, scope);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var storedHash = reader.GetString(0);
        var json = reader.GetString(1);
        await reader.CloseAsync();
        return string.Equals(storedHash, hash, StringComparison.Ordinal)
            ? new MissionCourierOfferResult(MissionCourierOfferOutcome.Replayed,
                JsonSerializer.Deserialize<MissionCourierOffer>(json))
            : new MissionCourierOfferResult(MissionCourierOfferOutcome.Conflict, null);
    }

    private static async Task SaveIdempotencyAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction,
        string scope, string operation, string key, string hash,
        MissionCourierOffer snapshot, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO mission_courier_offer_idempotency
                (scope_id, operation, idempotency_key, request_hash, response, created_at)
            VALUES (@scope, @operation, @key, @hash, @response::jsonb, @created_at);
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("scope", NpgsqlDbType.Text, scope);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, key);
        command.Parameters.AddWithValue("hash", NpgsqlDbType.Text, hash);
        command.Parameters.AddWithValue("response", NpgsqlDbType.Text, JsonSerializer.Serialize(snapshot));
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string RequireAdministrator()
    {
        var context = operationsContext;
        if (string.IsNullOrWhiteSpace(context.ActorId) ||
            !context.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return context.ActorId;
    }

    private DateTimeOffset UtcNow()
    {
        var value = _timeProvider.GetUtcNow();
        return new DateTimeOffset(value.Ticks - value.Ticks % 10, TimeSpan.Zero);
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static void Validate(Regex pattern, string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || !pattern.IsMatch(value))
            throw new ArgumentException("A mission courier offer field is invalid.", name);
    }
}
