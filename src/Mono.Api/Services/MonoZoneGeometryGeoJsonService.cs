using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Services;

/// <summary>
/// Returns a validated zone geometry snapshot as a GeoJSON Feature for internal map clients.
/// </summary>
public sealed class MonoZoneGeometryGeoJsonService(
    NpgsqlDataSource dataSource,
    PostgresZoneGeometryRepository geometryRepository,
    IMonoOperationsContext operationsContext)
{
    private const int MaximumCoordinates = 100_000;

    public async Task<ZoneGeometryGeoJsonFeature> GetAsync(
        string zoneId,
        string geometryVersion,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        zoneId = Required(zoneId, nameof(zoneId));
        geometryVersion = Required(geometryVersion, nameof(geometryVersion));

        var boundary = await geometryRepository.FindAsync(zoneId, geometryVersion, cancellationToken);
        if (boundary is null)
            throw new KeyNotFoundException("Zone geometry version was not found.");

        string geometryHash;
        string snapshotHash;
        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var command = new NpgsqlCommand("""
            SELECT geometry_sha256, snapshot_sha256
            FROM zone_geometry_versions
            WHERE zone_id = @zone_id AND geometry_version = @geometry_version;
            """, connection))
        {
            command.Parameters.AddWithValue("zone_id", NpgsqlDbType.Text, zoneId);
            command.Parameters.AddWithValue("geometry_version", NpgsqlDbType.Text, geometryVersion);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("Validated zone geometry snapshot disappeared.");
            geometryHash = reader.GetString(0);
            snapshotHash = reader.GetString(1);
        }

        var geometry = new WKBReader().Read(boundary.ToWellKnownBinary());
        geometry.SRID = 4326;
        if (geometry.NumPoints > MaximumCoordinates)
            throw new ArgumentException(
                $"The selected snapshot exceeds the {MaximumCoordinates} coordinate response limit.");

        var (type, coordinates) = geometry switch
        {
            Polygon polygon => ("Polygon", (object)PolygonCoordinates(polygon)),
            MultiPolygon multiPolygon => ("MultiPolygon", (object)MultiPolygonCoordinates(multiPolygon)),
            _ => throw new InvalidOperationException("Stored zone geometry must be Polygon or MultiPolygon.")
        };

        return new ZoneGeometryGeoJsonFeature(
            "Feature",
            new ZoneGeometryGeoJsonGeometry(type, coordinates),
            new ZoneGeometryGeoJsonProperties(
                boundary.ZoneId, boundary.Kind.ToString(), boundary.GeometryVersion,
                boundary.SourceId, boundary.SourceRevision, boundary.Attribution,
                geometryHash, snapshotHash, geometry.SRID, geometry.NumPoints));
    }

    private void RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
    }

    private static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);
        return value.Trim();
    }

    private static double[][][] PolygonCoordinates(Polygon polygon)
    {
        var rings = new double[polygon.NumInteriorRings + 1][][];
        rings[0] = Positions(polygon.ExteriorRing);
        for (var index = 0; index < polygon.NumInteriorRings; index++)
            rings[index + 1] = Positions(polygon.GetInteriorRingN(index));
        return rings;
    }

    private static double[][][][] MultiPolygonCoordinates(MultiPolygon multiPolygon)
    {
        var polygons = new double[multiPolygon.NumGeometries][][][];
        for (var index = 0; index < multiPolygon.NumGeometries; index++)
            polygons[index] = PolygonCoordinates((Polygon)multiPolygon.GetGeometryN(index));
        return polygons;
    }

    private static double[][] Positions(LineString ring) =>
        ring.Coordinates.Select(coordinate => new[] { coordinate.X, coordinate.Y }).ToArray();


}

public sealed record ZoneGeometryGeoJsonFeature(
    string Type,
    ZoneGeometryGeoJsonGeometry Geometry,
    ZoneGeometryGeoJsonProperties Properties);

public sealed record ZoneGeometryGeoJsonGeometry(
    string Type,
    object Coordinates);

public sealed record ZoneGeometryGeoJsonProperties(
    string ZoneId,
    string Kind,
    string GeometryVersion,
    string SourceId,
    string SourceRevision,
    string Attribution,
    string GeometryHash,
    string SnapshotHash,
    int Srid,
    int CoordinateCount);
