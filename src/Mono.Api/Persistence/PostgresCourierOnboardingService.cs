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
/// Durable MFA-gated courier onboarding operations. Stores operational metadata and
/// opaque evidence references only; personal documents are not accepted or retained.
/// </summary>
public sealed class PostgresCourierOnboardingService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    TimeProvider? timeProvider = null)
{
    private const string RegisterOperation = "REGISTER";
    private const string TransitionOperation = "TRUST_TRANSITION";
    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<CourierOperationsWriteResult> RegisterAsync(
        string mode,
        IReadOnlyCollection<string> preferredZoneIds,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireActor();
        mode = ValidateMode(mode);
        var zones = ValidateZoneIds(preferredZoneIds, required: true);
        idempotencyKey = ValidateIdempotencyKey(idempotencyKey);
        var requestHash = Hash(JsonSerializer.Serialize(new { mode, preferred_zone_ids = zones }));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockIdempotencyAsync(connection, transaction, actorId, RegisterOperation, idempotencyKey, cancellationToken);

        var replay = await ReadReplayAsync(
            connection, transaction, actorId, RegisterOperation, idempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        if (!await AreActiveZonesAsync(connection, transaction, zones, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierOperationsWriteResult(CourierOperationsWriteOutcome.InvalidZone, null);
        }

        var now = _timeProvider.GetUtcNow();
        var courierId = "crr_" + Guid.NewGuid().ToString("N");
        var snapshot = new CourierOperationsSnapshot(
            courierId, mode, "REGISTERED", zones, [], null, actorId, now, now, []);

        const string insertCourierSql = """
            INSERT INTO couriers (
                courier_id, mode, status, preferred_zone_ids, approved_zone_ids,
                primary_zone_id, created_by, created_at, updated_at)
            VALUES (@courier_id, @mode, 'REGISTERED', @preferred, '[]'::jsonb,
                    NULL, @created_by, @created_at, @updated_at);
            """;
        await using (var insert = new NpgsqlCommand(insertCourierSql, connection, transaction))
        {
            insert.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            insert.Parameters.AddWithValue("mode", NpgsqlDbType.Text, mode);
            insert.Parameters.AddWithValue("preferred", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(zones));
            insert.Parameters.AddWithValue("created_by", NpgsqlDbType.Text, actorId);
            insert.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
            insert.Parameters.AddWithValue("updated_at", NpgsqlDbType.TimestampTz, now);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await SaveIdempotencyAsync(
            connection, transaction, actorId, RegisterOperation, idempotencyKey,
            requestHash, snapshot, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierOperationsWriteResult(CourierOperationsWriteOutcome.Applied, snapshot);
    }

    public async Task<CourierOperationsWriteResult> AdvanceTrustAsync(
        string courierId,
        string targetStatus,
        IReadOnlyCollection<string>? approvedZoneIds,
        string? primaryZoneId,
        string reasonCode,
        string evidenceReference,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireActor();
        if (!CourierIdPattern.IsMatch(courierId))
            throw new ArgumentException("Courier ID is invalid.", nameof(courierId));
        var target = ParseTargetStatus(targetStatus);
        idempotencyKey = ValidateIdempotencyKey(idempotencyKey);
        var approved = target == CourierTrustStatus.Active
            ? ValidateZoneIds(approvedZoneIds, required: true)
            : ValidateZoneIds(approvedZoneIds, required: false);
        primaryZoneId = string.IsNullOrWhiteSpace(primaryZoneId) ? null : primaryZoneId.Trim();
        if (target != CourierTrustStatus.Active && (approved.Count > 0 || primaryZoneId is not null))
            throw new ArgumentException("Approved zones may only be supplied when activating a courier.");
        reasonCode = reasonCode?.Trim() ?? string.Empty;
        evidenceReference = evidenceReference?.Trim() ?? string.Empty;

        var requestHash = Hash(JsonSerializer.Serialize(new
        {
            courier_id = courierId,
            target_status = ToApiStatus(target),
            approved_zone_ids = approved,
            primary_zone_id = primaryZoneId,
            reason_code = reasonCode,
            evidence_reference = evidenceReference
        }));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockIdempotencyAsync(connection, transaction, actorId, TransitionOperation, idempotencyKey, cancellationToken);
        var replay = await ReadReplayAsync(
            connection, transaction, actorId, TransitionOperation, idempotencyKey, requestHash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        const string lockCourierSql = """
            SELECT mode, status, preferred_zone_ids::text, approved_zone_ids::text,
                   primary_zone_id, created_by, created_at, updated_at
            FROM couriers
            WHERE courier_id = @courier_id
            FOR UPDATE;
            """;
        string? mode = null;
        string? status = null;
        IReadOnlyList<string>? preferred = null;
        IReadOnlyList<string>? currentApproved = null;
        string? currentPrimary = null;
        string? createdBy = null;
        DateTimeOffset createdAt = default;
        DateTimeOffset updatedAt = default;
        await using (var command = new NpgsqlCommand(lockCourierSql, connection, transaction))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                mode = reader.GetString(0);
                status = reader.GetString(1);
                preferred = JsonSerializer.Deserialize<string[]>(reader.GetString(2)) ?? [];
                currentApproved = JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? [];
                currentPrimary = reader.IsDBNull(4) ? null : reader.GetString(4);
                createdBy = reader.GetString(5);
                createdAt = reader.GetFieldValue<DateTimeOffset>(6);
                updatedAt = reader.GetFieldValue<DateTimeOffset>(7);
            }
        }

        if (mode is null || status is null || preferred is null || currentApproved is null || createdBy is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierOperationsWriteResult(CourierOperationsWriteOutcome.CourierNotFound, null);
        }

        var fromStatus = ParseCurrentStatus(status);
        var nextApproval = target == CourierTrustStatus.Active
            ? new CourierZoneApproval
            {
                CourierId = courierId,
                PreferredZoneIds = preferred,
                ApprovedZoneIds = approved,
                PrimaryZoneId = primaryZoneId
            }
            : new CourierZoneApproval
            {
                CourierId = courierId,
                PreferredZoneIds = preferred,
                ApprovedZoneIds = currentApproved,
                PrimaryZoneId = currentPrimary
            };

        CourierTrustTransitionResult transitionResult;
        try
        {
            transitionResult = CourierTrustLifecycle.Advance(
                new CourierTrustProfile(courierId, mode, fromStatus, nextApproval),
                target, actorId, reasonCode, evidenceReference, _timeProvider.GetUtcNow());
        }
        catch (InvalidOperationException)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierOperationsWriteResult(CourierOperationsWriteOutcome.InvalidTransition, null);
        }
        catch (ArgumentException)
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierOperationsWriteResult(CourierOperationsWriteOutcome.InvalidTransition, null);
        }

        if (target == CourierTrustStatus.Active &&
            !await AreActiveZonesAsync(connection, transaction, approved, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return new CourierOperationsWriteResult(CourierOperationsWriteOutcome.InvalidZone, null);
        }

        var now = _timeProvider.GetUtcNow();
        var nextStatus = ToApiStatus(target);
        const string updateCourierSql = """
            UPDATE couriers
            SET status = @status, approved_zone_ids = @approved,
                primary_zone_id = @primary_zone_id, updated_at = @updated_at
            WHERE courier_id = @courier_id;
            """;
        await using (var update = new NpgsqlCommand(updateCourierSql, connection, transaction))
        {
            update.Parameters.AddWithValue("status", NpgsqlDbType.Text, nextStatus);
            update.Parameters.AddWithValue("approved", NpgsqlDbType.Jsonb,
                JsonSerializer.Serialize(transitionResult.Profile.ZoneApproval.ApprovedZoneIds));
            update.Parameters.AddWithValue("primary_zone_id", NpgsqlDbType.Text,
                (object?)transitionResult.Profile.ZoneApproval.PrimaryZoneId ?? DBNull.Value);
            update.Parameters.AddWithValue("updated_at", NpgsqlDbType.TimestampTz, now);
            update.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        var transitionId = "ctt_" + Guid.NewGuid().ToString("N");
        const string insertTransitionSql = """
            INSERT INTO courier_trust_transitions (
                transition_id, courier_id, from_status, to_status, actor_id,
                reason_code, evidence_reference, occurred_at)
            VALUES (@transition_id, @courier_id, @from_status, @to_status, @actor_id,
                    @reason_code, @evidence_reference, @occurred_at);
            """;
        await using (var insert = new NpgsqlCommand(insertTransitionSql, connection, transaction))
        {
            insert.Parameters.AddWithValue("transition_id", NpgsqlDbType.Text, transitionId);
            insert.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            insert.Parameters.AddWithValue("from_status", NpgsqlDbType.Text, ToApiStatus(transitionResult.Transition.FromStatus));
            insert.Parameters.AddWithValue("to_status", NpgsqlDbType.Text, nextStatus);
            insert.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            insert.Parameters.AddWithValue("reason_code", NpgsqlDbType.Text, transitionResult.Transition.ReasonCode);
            insert.Parameters.AddWithValue("evidence_reference", NpgsqlDbType.Text, transitionResult.Transition.EvidenceReference);
            insert.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, transitionResult.Transition.OccurredAt);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        var transitions = await ReadTransitionsAsync(connection, transaction, courierId, cancellationToken);
        var snapshot = new CourierOperationsSnapshot(
            courierId, mode, nextStatus,
            transitionResult.Profile.ZoneApproval.PreferredZoneIds.ToArray(),
            transitionResult.Profile.ZoneApproval.ApprovedZoneIds.ToArray(),
            transitionResult.Profile.ZoneApproval.PrimaryZoneId,
            createdBy, createdAt, now, transitions);
        await SaveIdempotencyAsync(
            connection, transaction, actorId, TransitionOperation, idempotencyKey,
            requestHash, snapshot, now, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CourierOperationsWriteResult(CourierOperationsWriteOutcome.Applied, snapshot);
    }

    public async Task<CourierOperationsSnapshot?> FindAsync(
        string courierId,
        CancellationToken cancellationToken = default)
    {
        RequireActor();
        if (!CourierIdPattern.IsMatch(courierId))
            throw new ArgumentException("Courier ID is invalid.", nameof(courierId));

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        const string sql = """
            SELECT mode, status, preferred_zone_ids::text, approved_zone_ids::text,
                   primary_zone_id, created_by, created_at, updated_at
            FROM couriers
            WHERE courier_id = @courier_id;
            """;
        string? mode = null;
        string? status = null;
        IReadOnlyList<string>? preferred = null;
        IReadOnlyList<string>? approved = null;
        string? primary = null;
        string? createdBy = null;
        DateTimeOffset createdAt = default;
        DateTimeOffset updatedAt = default;
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                mode = reader.GetString(0);
                status = reader.GetString(1);
                preferred = JsonSerializer.Deserialize<string[]>(reader.GetString(2)) ?? [];
                approved = JsonSerializer.Deserialize<string[]>(reader.GetString(3)) ?? [];
                primary = reader.IsDBNull(4) ? null : reader.GetString(4);
                createdBy = reader.GetString(5);
                createdAt = reader.GetFieldValue<DateTimeOffset>(6);
                updatedAt = reader.GetFieldValue<DateTimeOffset>(7);
            }
        }

        if (mode is null || status is null || preferred is null || approved is null || createdBy is null)
            return null;

        var transitions = await ReadTransitionsAsync(connection, null, courierId, cancellationToken);
        return new CourierOperationsSnapshot(
            courierId, mode, status, preferred, approved, primary, createdBy, createdAt, updatedAt, transitions);
    }

    private async Task<IReadOnlyList<CourierOperationsTransitionView>> ReadTransitionsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string courierId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT transition_id, from_status, to_status, actor_id, reason_code,
                   evidence_reference, occurred_at
            FROM courier_trust_transitions
            WHERE courier_id = @courier_id
            ORDER BY occurred_at, transition_id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var transitions = new List<CourierOperationsTransitionView>();
        while (await reader.ReadAsync(cancellationToken))
        {
            transitions.Add(new CourierOperationsTransitionView(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)));
        }
        return transitions;
    }

    private static async Task<bool> AreActiveZonesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyCollection<string> zoneIds,
        CancellationToken cancellationToken)
    {
        if (zoneIds.Count == 0)
            return false;
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM zone_geometry_active
            WHERE zone_id = ANY(@zone_ids);
            """, connection, transaction);
        command.Parameters.AddWithValue("zone_ids", NpgsqlDbType.Array | NpgsqlDbType.Text, zoneIds.ToArray());
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == zoneIds.Count;
    }

    private async Task<CourierOperationsWriteResult?> ReadReplayAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string actorId,
        string operation,
        string key,
        string requestHash,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT request_hash, response::text
            FROM courier_operations_idempotency
            WHERE actor_id = @actor_id AND operation = @operation AND idempotency_key = @key;
            """, connection, transaction);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;
        var sameRequest = string.Equals(reader.GetString(0), requestHash, StringComparison.Ordinal);
        var snapshot = JsonSerializer.Deserialize<CourierOperationsSnapshot>(reader.GetString(1));
        await reader.DisposeAsync();
        return sameRequest && snapshot is not null
            ? new CourierOperationsWriteResult(CourierOperationsWriteOutcome.Replayed, snapshot)
            : new CourierOperationsWriteResult(CourierOperationsWriteOutcome.Conflict, null);
    }

    private static async Task LockIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string actorId,
        string operation,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", connection, transaction);
        command.Parameters.AddWithValue("lock_key", NpgsqlDbType.Text, $"{actorId}:{operation}:{key}");
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SaveIdempotencyAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string actorId,
        string operation,
        string key,
        string requestHash,
        CourierOperationsSnapshot snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO courier_operations_idempotency (
                actor_id, operation, idempotency_key, request_hash, response, created_at)
            VALUES (@actor_id, @operation, @key, @request_hash, @response, @created_at);
            """, connection, transaction);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("operation", NpgsqlDbType.Text, operation);
        command.Parameters.AddWithValue("key", NpgsqlDbType.Text, key);
        command.Parameters.AddWithValue("request_hash", NpgsqlDbType.Text, requestHash);
        command.Parameters.AddWithValue("response", NpgsqlDbType.Jsonb, JsonSerializer.Serialize(snapshot));
        command.Parameters.AddWithValue("created_at", NpgsqlDbType.TimestampTz, now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string RequireActor()
    {
        var actor = operationsContext.ActorId;
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 128 || actor.Any(char.IsControl))
            throw new InvalidOperationException("A valid authenticated Mono administrator actor is required.");
        return actor.Trim();
    }

    private static string ValidateMode(string? value)
    {
        var mode = value?.Trim() ?? string.Empty;
        if (mode.Length is < 2 or > 32 ||
            mode[0] is < 'A' or > 'Z' ||
            mode.Any(character =>
                (character is < 'A' or > 'Z') &&
                (character is < '0' or > '9') &&
                character != '_'))
            throw new ArgumentException("Mode must be a canonical uppercase identifier.", nameof(value));
        return mode;
    }

    private static IReadOnlyList<string> ValidateZoneIds(
        IReadOnlyCollection<string>? values,
        bool required)
    {
        if (values is null)
        {
            if (required)
                throw new ArgumentException("Zone IDs are required.", nameof(values));
            return [];
        }

        var zones = values.Select(value => value?.Trim() ?? string.Empty).ToArray();
        if ((required && zones.Length == 0) ||
            zones.Any(zone => zone.Length is < 1 or > 128 || zone.Any(char.IsControl)) ||
            zones.Distinct(StringComparer.Ordinal).Count() != zones.Length)
            throw new ArgumentException("Zone IDs must be non-empty, bounded, and unique.", nameof(values));
        return zones;
    }

    private static string ValidateIdempotencyKey(string? value)
    {
        var key = value?.Trim() ?? string.Empty;
        if (key.Length is < 1 or > 128 || key.Any(char.IsControl))
            throw new ArgumentException("A valid Idempotency-Key is required.", nameof(value));
        return key;
    }

    private static CourierTrustStatus ParseTargetStatus(string? status)
    {
        if (!Enum.TryParse<CourierTrustStatus>(status, ignoreCase: true, out var parsed) ||
            !string.Equals(ToApiStatus(parsed), status, StringComparison.Ordinal))
            throw new ArgumentException("Target status is invalid.", nameof(status));
        return parsed;
    }

    private static CourierTrustStatus ParseCurrentStatus(string status) =>
        Enum.GetValues<CourierTrustStatus>().Single(value =>
            string.Equals(ToApiStatus(value), status, StringComparison.Ordinal));

    private static string ToApiStatus(CourierTrustStatus status) => status switch
    {
        CourierTrustStatus.Registered => "REGISTERED",
        CourierTrustStatus.IdentityVerified => "IDENTITY_VERIFIED",
        CourierTrustStatus.DocumentsSubmitted => "DOCUMENTS_SUBMITTED",
        CourierTrustStatus.TrustRequirementsVerified => "TRUST_REQUIREMENTS_VERIFIED",
        CourierTrustStatus.MonoApproved => "MONO_APPROVED",
        CourierTrustStatus.Active => "ACTIVE",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
