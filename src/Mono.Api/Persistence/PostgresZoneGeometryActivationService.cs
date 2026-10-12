using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>
/// Records administrative activation state for immutable zone geometry snapshots.
/// This state is not consumed by delivery eligibility or public routing.
/// </summary>
public sealed class PostgresZoneGeometryActivationService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ZoneGeometryActivationState?> GetCurrentAsync(
        string zoneId,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        zoneId = Required(zoneId, nameof(zoneId), 128);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT zone_id, geometry_version, snapshot_sha256, activated_at, activated_by
            FROM zone_geometry_active
            WHERE zone_id = @zone_id;
            """, connection);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ZoneGeometryActivationState(reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4))
            : null;
    }

    public async Task<IReadOnlyList<ZoneGeometryActivationEvent>> ListHistoryAsync(
        string zoneId,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        zoneId = Required(zoneId, nameof(zoneId), 128);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT activation_event_id, zone_id, geometry_version, snapshot_sha256,
                   action, actor_id, reason, occurred_at
            FROM zone_geometry_activation_events
            WHERE zone_id = @zone_id
            ORDER BY activation_event_id;
            """, connection);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<ZoneGeometryActivationEvent>();
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(new ZoneGeometryActivationEvent(reader.GetInt64(0), reader.GetString(1),
                reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7)));
        return entries;
    }

    public async Task<ZoneGeometryActivationState> ActivateAsync(
        string zoneId,
        string geometryVersion,
        string expectedSnapshotSha256,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        zoneId = Required(zoneId, nameof(zoneId), 128);
        geometryVersion = Required(geometryVersion, nameof(geometryVersion), 128);
        var expectedHash = NormalizeHash(expectedSnapshotSha256);
        reason = Required(reason, nameof(reason), 1000);
        var now = _timeProvider.GetUtcNow();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockZoneAsync(connection, transaction, zoneId, cancellationToken);
        var target = await FindSnapshotAsync(connection, transaction, zoneId, geometryVersion, cancellationToken);
        if (target is null)
            throw new ZoneGeometryActivationSnapshotNotFoundException();
        if (!string.Equals(target, expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new ZoneGeometryActivationHashConflictException();
        if (!await IsLatestReviewApprovedAsync(connection, transaction, zoneId, geometryVersion, cancellationToken))
            throw new ZoneGeometryActivationReviewRequiredException();

        var current = await FindCurrentAsync(connection, transaction, zoneId, cancellationToken);
        if (current is not null && current.GeometryVersion == geometryVersion)
            throw new ZoneGeometryActivationStateConflictException("This geometry version is already active.");

        if (current is not null)
        {
            await InsertEventAsync(connection, transaction, zoneId, current.GeometryVersion,
                current.SnapshotSha256, "DEACTIVATE", actorId, "Replaced by " + geometryVersion, now, cancellationToken);
        }

        await InsertEventAsync(connection, transaction, zoneId, geometryVersion,
            target, "ACTIVATE", actorId, reason, now, cancellationToken);
        await using (var update = new NpgsqlCommand("""
            INSERT INTO zone_geometry_active (zone_id, geometry_version, snapshot_sha256, activated_at, activated_by)
            VALUES (@zone_id, @geometry_version, @snapshot_sha256, @activated_at, @activated_by)
            ON CONFLICT (zone_id) DO UPDATE SET
                geometry_version = EXCLUDED.geometry_version,
                snapshot_sha256 = EXCLUDED.snapshot_sha256,
                activated_at = EXCLUDED.activated_at,
                activated_by = EXCLUDED.activated_by;
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            update.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);
            update.Parameters.AddWithValue("snapshot_sha256", NpgsqlDbType.Text, target);
            update.Parameters.AddWithValue("activated_at", NpgsqlDbType.TimestampTz, now);
            update.Parameters.AddWithValue("activated_by", NpgsqlDbType.Text, actorId);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new ZoneGeometryActivationState(zoneId, geometryVersion, target, now, actorId);
    }

    public async Task<bool> DeactivateAsync(
        string zoneId,
        string geometryVersion,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        zoneId = Required(zoneId, nameof(zoneId), 128);
        geometryVersion = Required(geometryVersion, nameof(geometryVersion), 128);
        reason = Required(reason, nameof(reason), 1000);
        var now = _timeProvider.GetUtcNow();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await LockZoneAsync(connection, transaction, zoneId, cancellationToken);
        var current = await FindCurrentAsync(connection, transaction, zoneId, cancellationToken);
        if (current is null)
            throw new ZoneGeometryActivationSnapshotNotFoundException();
        if (!string.Equals(current.GeometryVersion, geometryVersion, StringComparison.Ordinal))
            throw new ZoneGeometryActivationStateConflictException("The requested version is not currently active.");

        await InsertEventAsync(connection, transaction, zoneId, geometryVersion,
            current.SnapshotSha256, "DEACTIVATE", actorId, reason, now, cancellationToken);
        await using (var delete = new NpgsqlCommand(
            "DELETE FROM zone_geometry_active WHERE zone_id = @zone_id AND geometry_version = @geometry_version;",
            connection, transaction))
        {
            delete.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            delete.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);
            if (await delete.ExecuteNonQueryAsync(cancellationToken) != 1)
                throw new ZoneGeometryActivationStateConflictException("Active geometry changed during deactivation.");
        }
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static async Task LockZoneAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string zoneId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtext(@zone_id));", connection, transaction);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<string?> FindSnapshotAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string zoneId,
        string geometryVersion, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT snapshot_sha256 FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @geometry_version
            FOR KEY SHARE;
            """, connection, transaction);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        command.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);
        return (string?)await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<bool> IsLatestReviewApprovedAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string zoneId,
        string geometryVersion, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT decision FROM zone_geometry_reviews
            WHERE zone_id = @zone_id AND geometry_version = @geometry_version
            ORDER BY review_id DESC LIMIT 1;
            """, connection, transaction);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        command.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);
        return string.Equals((string?)await command.ExecuteScalarAsync(cancellationToken),
            "APPROVE", StringComparison.Ordinal);
    }

    private static async Task<ZoneGeometryActivationState?> FindCurrentAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string zoneId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT zone_id, geometry_version, snapshot_sha256, activated_at, activated_by
            FROM zone_geometry_active WHERE zone_id = @zone_id FOR UPDATE;
            """, connection, transaction);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ZoneGeometryActivationState(reader.GetString(0), reader.GetString(1),
                reader.GetString(2), reader.GetFieldValue<DateTimeOffset>(3), reader.GetString(4))
            : null;
    }

    private static async Task InsertEventAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string zoneId,
        string geometryVersion, string snapshotHash, string action, string actorId, string reason,
        DateTimeOffset occurredAt, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO zone_geometry_activation_events (
                zone_id, geometry_version, snapshot_sha256, action, actor_id, reason, occurred_at)
            VALUES (@zone_id, @geometry_version, @snapshot_sha256, @action, @actor_id, @reason, @occurred_at);
            """, connection, transaction);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        command.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);
        command.Parameters.AddWithValue("snapshot_sha256", NpgsqlDbType.Text, snapshotHash);
        command.Parameters.AddWithValue("action", NpgsqlDbType.Text, action);
        command.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
        command.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason);
        command.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private string RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return operationsContext.ActorId;
    }

    private static string NormalizeHash(string value)
    {
        var normalized = Required(value, nameof(value), 64).ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"\A[0-9a-f]{64}\z"))
            throw new ArgumentException("Snapshot hash must be a SHA-256 hex digest.", nameof(value));
        return normalized;
    }

    private static string Required(string? value, string parameterName, int maximumLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentException("Value exceeds its maximum length.", parameterName);
        return normalized;
    }
}

public sealed record ZoneGeometryActivationState(
    string ZoneId,
    string GeometryVersion,
    string SnapshotSha256,
    DateTimeOffset ActivatedAt,
    string ActivatedBy);

public sealed record ZoneGeometryActivationEvent(
    long ActivationEventId,
    string ZoneId,
    string GeometryVersion,
    string SnapshotSha256,
    string Action,
    string ActorId,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed class ZoneGeometryActivationSnapshotNotFoundException : Exception { }
public sealed class ZoneGeometryActivationHashConflictException : Exception { }
public sealed class ZoneGeometryActivationReviewRequiredException : Exception { }

public sealed class ZoneGeometryActivationStateConflictException(string message) : Exception(message)
{
}
