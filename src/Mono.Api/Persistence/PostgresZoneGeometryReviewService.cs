using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

/// <summary>
/// Append-only human review history for immutable zone geometry snapshots.
/// Reviews do not activate geometry or affect runtime eligibility.
/// </summary>
public sealed class PostgresZoneGeometryReviewService(
    NpgsqlDataSource dataSource,
    IMonoOperationsContext operationsContext,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async Task<ZoneGeometryReviewEntry> RecordAsync(
        string zoneId,
        string geometryVersion,
        string expectedSnapshotSha256,
        string decision,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var actorId = RequireAdministrator();
        zoneId = Required(zoneId, nameof(zoneId), 128);
        geometryVersion = Required(geometryVersion, nameof(geometryVersion), 128);
        expectedSnapshotSha256 = Required(expectedSnapshotSha256, nameof(expectedSnapshotSha256), 64).ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(expectedSnapshotSha256, @"\A[0-9a-f]{64}\z"))
            throw new ArgumentException("Snapshot hash must be a SHA-256 hex digest.", nameof(expectedSnapshotSha256));
        decision = Required(decision, nameof(decision), 16).ToUpperInvariant();
        if (decision is not ("APPROVE" or "REJECT"))
            throw new ArgumentException("Decision must be APPROVE or REJECT.", nameof(decision));
        reason = Required(reason, nameof(reason), 1000);
        var occurredAt = _timeProvider.GetUtcNow();

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        string? storedHash;
        await using (var find = new NpgsqlCommand("""
            SELECT snapshot_sha256
            FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @geometry_version
            FOR KEY SHARE;
            """, connection, transaction))
        {
            find.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            find.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);
            storedHash = (string?)await find.ExecuteScalarAsync(cancellationToken);
        }

        if (storedHash is null)
            throw new ZoneGeometrySnapshotNotFoundException();
        if (!string.Equals(storedHash, expectedSnapshotSha256, StringComparison.OrdinalIgnoreCase))
            throw new ZoneGeometrySnapshotHashConflictException();

        const string insertSql = """
            INSERT INTO zone_geometry_reviews (
                zone_id, geometry_version, snapshot_sha256, decision, actor_id, reason, occurred_at)
            VALUES (@zone_id, @geometry_version, @snapshot_sha256, @decision, @actor_id, @reason, @occurred_at)
            RETURNING review_id;
            """;
        long reviewId;
        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            insert.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);
            insert.Parameters.AddWithValue("snapshot_sha256", NpgsqlDbType.Text, storedHash);
            insert.Parameters.AddWithValue("decision", NpgsqlDbType.Text, decision);
            insert.Parameters.AddWithValue("actor_id", NpgsqlDbType.Text, actorId);
            insert.Parameters.AddWithValue("reason", NpgsqlDbType.Text, reason);
            insert.Parameters.AddWithValue("occurred_at", NpgsqlDbType.TimestampTz, occurredAt);
            reviewId = (long)(await insert.ExecuteScalarAsync(cancellationToken)
                ?? throw new InvalidOperationException("Review entry was not created."));
        }
        await transaction.CommitAsync(cancellationToken);
        return new ZoneGeometryReviewEntry(reviewId, zoneId, geometryVersion, storedHash,
            decision, actorId, reason, occurredAt);
    }

    public async Task<IReadOnlyList<ZoneGeometryReviewEntry>?> ListAsync(
        string zoneId,
        string geometryVersion,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        zoneId = Required(zoneId, nameof(zoneId), 128);
        geometryVersion = Required(geometryVersion, nameof(geometryVersion), 128);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var exists = new NpgsqlCommand("""
            SELECT EXISTS (
                SELECT 1 FROM zone_geometry_versions
                WHERE zone_id = @zone_id AND geometry_version = @geometry_version);
            """, connection))
        {
            exists.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            exists.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);
            if (!Equals(await exists.ExecuteScalarAsync(cancellationToken), true))
                return null;
        }

        await using var command = new NpgsqlCommand("""
            SELECT review_id, zone_id, geometry_version, snapshot_sha256,
                   decision, actor_id, reason, occurred_at
            FROM zone_geometry_reviews
            WHERE zone_id = @zone_id AND geometry_version = @geometry_version
            ORDER BY review_id;
            """, connection);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        command.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var entries = new List<ZoneGeometryReviewEntry>();
        while (await reader.ReadAsync(cancellationToken))
            entries.Add(new ZoneGeometryReviewEntry(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetString(6), reader.GetFieldValue<DateTimeOffset>(7)));
        return entries;
    }

    private string RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
        return operationsContext.ActorId;
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

public sealed record ZoneGeometryReviewEntry(
    long ReviewId,
    string ZoneId,
    string GeometryVersion,
    string SnapshotSha256,
    string Decision,
    string ActorId,
    string Reason,
    DateTimeOffset OccurredAt);

public sealed class ZoneGeometrySnapshotNotFoundException : Exception;
public sealed class ZoneGeometrySnapshotHashConflictException : Exception;
