using System.Text;
using System.Text.Json.Serialization;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Services;

/// <summary>
/// Builds a paginated GeoJSON FeatureCollection from the active immutable snapshot pointer
/// for one zone hierarchy level.
/// </summary>
public sealed class MonoActiveZoneGeoJsonLayerService(
    NpgsqlDataSource dataSource,
    MonoZoneGeometryGeoJsonService geoJsonService,
    IMonoOperationsContext operationsContext)
{
    private const int DefaultPageSize = 10;
    private const int MaximumPageSize = 25;
    private const int MaximumPageCoordinates = 250_000;

    public async Task<MonoActiveZoneGeoJsonLayerPage> GetAsync(
        string kind,
        int? pageSize,
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        if (!Enum.TryParse<ZoneKind>(kind, ignoreCase: false, out var zoneKind) ||
            !Enum.IsDefined(typeof(ZoneKind), zoneKind))
            throw new ArgumentException("Unsupported zone kind.", nameof(kind));

        var size = pageSize ?? DefaultPageSize;
        if (size is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(
                nameof(pageSize), $"Page size must be between 1 and {MaximumPageSize}.");
        var afterZoneId = DecodeCursor(cursor);

        var rows = new List<ActiveSnapshotRow>(size + 1);
        const string sql = """
            SELECT a.zone_id, a.geometry_version, a.snapshot_sha256, g.snapshot_sha256
            FROM zone_geometry_active a
            JOIN zone_geometry_versions g
              ON g.zone_id = a.zone_id AND g.geometry_version = a.geometry_version
            WHERE g.zone_kind = @zone_kind
              AND (@after_zone_id IS NULL OR a.zone_id > @after_zone_id)
            ORDER BY a.zone_id
            LIMIT @take;
            """;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("zone_kind", NpgsqlDbType.Text, zoneKind.ToString());
            command.Parameters.AddWithValue("after_zone_id", NpgsqlDbType.Text,
                afterZoneId is null ? DBNull.Value : afterZoneId);
            command.Parameters.AddWithValue("take", NpgsqlDbType.Integer, size + 1);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                rows.Add(new ActiveSnapshotRow(
                    reader.GetString(0), reader.GetString(1),
                    reader.GetString(2), reader.GetString(3)));
        }

        var hasMore = rows.Count > size;
        if (hasMore)
            rows.RemoveAt(rows.Count - 1);

        var features = new List<ZoneGeometryGeoJsonFeature>(rows.Count);
        var coordinateCount = 0;
        foreach (var row in rows)
        {
            if (!string.Equals(row.PointerHash, row.SnapshotHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Active geometry pointer hash does not match snapshot for {row.ZoneId}.");

            var feature = await geoJsonService.GetAsync(
                row.ZoneId, row.GeometryVersion, cancellationToken);
            if (feature.Properties.Kind != zoneKind.ToString() ||
                !string.Equals(feature.Properties.SnapshotHash, row.SnapshotHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Active GeoJSON feature does not match its selected snapshot.");

            coordinateCount += feature.Properties.CoordinateCount;
            if (coordinateCount > MaximumPageCoordinates)
                throw new ZoneGeometryGeoJsonPageTooLargeException(
                    $"The selected page exceeds the {MaximumPageCoordinates} coordinate response limit.");
            features.Add(feature);
        }

        var nextCursor = hasMore && rows.Count > 0 ? EncodeCursor(rows[^1].ZoneId) : null;
        var collection = new ZoneGeometryGeoJsonFeatureCollection(
            "FeatureCollection", features, MonoGeoJsonBounds.FromFeatures(features));
        return new MonoActiveZoneGeoJsonLayerPage(
            collection, zoneKind.ToString(), features.Count, coordinateCount, hasMore, nextCursor);
    }

    private void RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
    }

    private static string? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
            return null;
        if (cursor.Length > 1024)
            throw new ArgumentException("Layer cursor is invalid.", nameof(cursor));

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            var zoneId = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(padded));
            if (string.IsNullOrWhiteSpace(zoneId) || zoneId.Length > 128 ||
                zoneId != zoneId.Trim() || zoneId.Any(char.IsControl))
                throw new FormatException();
            return zoneId;
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException)
        {
            throw new ArgumentException("Layer cursor is invalid.", nameof(cursor));
        }
    }

    private static string EncodeCursor(string zoneId) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(zoneId))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record ActiveSnapshotRow(
        string ZoneId,
        string GeometryVersion,
        string PointerHash,
        string SnapshotHash);
}

public sealed record ZoneGeometryGeoJsonFeatureCollection(
    string Type,
    IReadOnlyList<ZoneGeometryGeoJsonFeature> Features,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double[]? Bbox = null);

public sealed record MonoActiveZoneGeoJsonLayerPage(
    ZoneGeometryGeoJsonFeatureCollection GeoJson,
    string Kind,
    int FeatureCount,
    int CoordinateCount,
    bool HasMore,
    string? NextCursor);

public sealed class ZoneGeometryGeoJsonPageTooLargeException(string message)
    : Exception(message);
