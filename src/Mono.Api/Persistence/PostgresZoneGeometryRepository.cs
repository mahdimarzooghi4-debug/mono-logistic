using System.Security.Cryptography;
using System.Text;
using Mono.Api.Domain;
using NetTopologySuite.IO;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Persistence;

public enum ZoneGeometryWriteOutcome
{
    Persisted,
    Replayed,
    Conflict
}

public sealed record ZoneGeometryWriteResult(
    ZoneGeometryWriteOutcome Outcome,
    string ZoneId,
    string GeometryVersion);

/// <summary>
/// Stores immutable, globally defined zone-geometry snapshots. This repository does not
/// activate a version or make it authoritative for delivery eligibility.
/// </summary>
public sealed class PostgresZoneGeometryRepository
{
    private const int Wgs84Srid = 4326;
    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeProvider _timeProvider;

    public PostgresZoneGeometryRepository(
        NpgsqlDataSource dataSource,
        TimeProvider? timeProvider = null)
    {
        _dataSource = dataSource;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<ZoneGeometryWriteResult> PersistAsync(
        ZoneGeometryBoundary boundary,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        var wkb = boundary.ToWellKnownBinary();
        var geometryHash = HashHex(wkb);
        var snapshotHash = ComputeSnapshotHash(boundary, wkb);
        var recordedAt = _timeProvider.GetUtcNow();

        const string insertSql = """
            INSERT INTO zone_geometry_versions (
                zone_id, zone_kind, geometry_version, source_id, source_revision,
                attribution, geometry_srid, geometry_type, geometry_wkb,
                geometry_sha256, snapshot_sha256, recorded_at)
            VALUES (
                @zone_id, @zone_kind, @geometry_version, @source_id, @source_revision,
                @attribution, @geometry_srid, @geometry_type, @geometry_wkb,
                @geometry_sha256, @snapshot_sha256, @recorded_at)
            ON CONFLICT (zone_id, geometry_version) DO NOTHING;
            """;

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var insert = new NpgsqlCommand(insertSql, connection);
        insert.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, boundary.ZoneId);
        insert.Parameters.AddWithValue("zone_kind", NpgsqlDbType.Text, boundary.Kind.ToString());
        insert.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, boundary.GeometryVersion);
        insert.Parameters.AddWithValue("source_id", NpgsqlDbType.Text, boundary.SourceId);
        insert.Parameters.AddWithValue("source_revision", NpgsqlDbType.Text, boundary.SourceRevision);
        insert.Parameters.AddWithValue("attribution", NpgsqlDbType.Text, boundary.Attribution);
        insert.Parameters.AddWithValue("geometry_srid", NpgsqlDbType.Integer, Wgs84Srid);
        insert.Parameters.AddWithValue("geometry_type", NpgsqlDbType.Text, boundary.GeometryType);
        insert.Parameters.AddWithValue("geometry_wkb", NpgsqlDbType.Bytea, wkb);
        insert.Parameters.AddWithValue("geometry_sha256", NpgsqlDbType.Text, geometryHash);
        insert.Parameters.AddWithValue("snapshot_sha256", NpgsqlDbType.Text, snapshotHash);
        insert.Parameters.AddWithValue("recorded_at", NpgsqlDbType.TimestampTz, recordedAt);

        if (await insert.ExecuteNonQueryAsync(cancellationToken) == 1)
            return new ZoneGeometryWriteResult(
                ZoneGeometryWriteOutcome.Persisted, boundary.ZoneId, boundary.GeometryVersion);

        var existing = await FindAsync(
            connection, boundary.ZoneId, boundary.GeometryVersion, cancellationToken);
        if (existing is null)
            throw new InvalidOperationException("The conflicting zone geometry snapshot could not be read.");

        var existingWkb = existing.ToWellKnownBinary();
        var outcome = CryptographicOperations.FixedTimeEquals(
                          Convert.FromHexString(ComputeSnapshotHash(existing, existingWkb)),
                          Convert.FromHexString(snapshotHash))
            ? ZoneGeometryWriteOutcome.Replayed
            : ZoneGeometryWriteOutcome.Conflict;

        return new ZoneGeometryWriteResult(outcome, existing.ZoneId, existing.GeometryVersion);
    }

    public async Task<ZoneGeometryBoundary?> FindAsync(
        string zoneId,
        string geometryVersion,
        CancellationToken cancellationToken = default)
    {
        zoneId = Required(zoneId, nameof(zoneId));
        geometryVersion = Required(geometryVersion, nameof(geometryVersion));

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        return await FindAsync(connection, zoneId, geometryVersion, cancellationToken);
    }

    private static async Task<ZoneGeometryBoundary?> FindAsync(
        NpgsqlConnection connection,
        string zoneId,
        string geometryVersion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT zone_id, zone_kind, geometry_version, source_id, source_revision,
                   attribution, geometry_srid, geometry_type, geometry_wkb,
                   geometry_sha256, snapshot_sha256
            FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @geometry_version;
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
        command.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        try
        {
            var storedZoneId = reader.GetString(0);
            var kindName = reader.GetString(1);
            var storedVersion = reader.GetString(2);
            var sourceId = reader.GetString(3);
            var sourceRevision = reader.GetString(4);
            var attribution = reader.GetString(5);
            var srid = reader.GetInt32(6);
            var expectedGeometryType = reader.GetString(7);
            var wkb = reader.GetFieldValue<byte[]>(8);
            var expectedGeometryHash = reader.GetString(9);
            var expectedSnapshotHash = reader.GetString(10);

            if (!Enum.TryParse<ZoneKind>(kindName, ignoreCase: false, out var kind) ||
                !Enum.IsDefined(typeof(ZoneKind), kind))
                throw new InvalidOperationException("Stored zone kind is invalid.");
            if (srid != Wgs84Srid)
                throw new InvalidOperationException("Stored zone geometry SRID is invalid.");
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedGeometryHash),
                    SHA256.HashData(wkb)))
                throw new InvalidOperationException("Stored zone geometry hash does not match.");

            var geometry = new WKBReader().Read(wkb);
            geometry.SRID = srid;
            if (!string.Equals(geometry.GeometryType, expectedGeometryType, StringComparison.Ordinal))
                throw new InvalidOperationException("Stored zone geometry type does not match.");

            var boundary = ZoneGeometryBoundary.Create(
                storedZoneId, kind, storedVersion, sourceId, sourceRevision, attribution, geometry);
            var actualSnapshotHash = ComputeSnapshotHash(boundary, boundary.ToWellKnownBinary());
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(expectedSnapshotHash),
                    Convert.FromHexString(actualSnapshotHash)))
                throw new InvalidOperationException("Stored zone geometry snapshot hash does not match.");

            return boundary;
        }
        catch (Exception exception) when (
            exception is FormatException or ArgumentException or InvalidDataException)
        {
            throw new InvalidOperationException("Stored zone geometry snapshot is invalid.", exception);
        }
    }

    private static string ComputeSnapshotHash(ZoneGeometryBoundary boundary, byte[] wkb)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(boundary.ZoneId);
            writer.Write((int)boundary.Kind);
            writer.Write(boundary.GeometryVersion);
            writer.Write(boundary.SourceId);
            writer.Write(boundary.SourceRevision);
            writer.Write(boundary.Attribution);
            writer.Write(Wgs84Srid);
            writer.Write(wkb.Length);
            writer.Write(wkb);
        }

        return HashHex(stream.ToArray());
    }

    private static string HashHex(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);
        return value.Trim();
    }
}
