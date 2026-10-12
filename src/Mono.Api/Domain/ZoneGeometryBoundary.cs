using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace Mono.Api.Domain;

public enum ZonePointRelation
{
    Outside,
    Inside,
    Boundary
}

public enum ZonePointResolutionStatus
{
    Matched,
    Outside,
    Boundary,
    Ambiguous
}

/// <summary>
/// Immutable, validated WGS84 boundary snapshot for one operational zone.
/// Polygon coordinates use GeoJSON order: X=longitude, Y=latitude.
/// </summary>
public sealed class ZoneGeometryBoundary
{
    private readonly Geometry _geometry;

    private ZoneGeometryBoundary(
        string zoneId,
        ZoneKind kind,
        string geometryVersion,
        string sourceId,
        string sourceRevision,
        string attribution,
        Geometry geometry)
    {
        ZoneId = zoneId;
        Kind = kind;
        GeometryVersion = geometryVersion;
        SourceId = sourceId;
        SourceRevision = sourceRevision;
        Attribution = attribution;
        _geometry = (Geometry)geometry.Copy();
        GeometryType = _geometry.GeometryType;
    }

    public string ZoneId { get; }
    public ZoneKind Kind { get; }
    public string GeometryVersion { get; }
    public string SourceId { get; }
    public string SourceRevision { get; }
    public string Attribution { get; }
    public string GeometryType { get; }

    internal byte[] ToWellKnownBinary() => new WKBWriter().Write(_geometry);

    public static ZoneGeometryBoundary Create(
        string zoneId,
        ZoneKind kind,
        string geometryVersion,
        string sourceId,
        string sourceRevision,
        string attribution,
        Geometry geometry)
    {
        zoneId = Required(zoneId, nameof(zoneId));
        geometryVersion = Required(geometryVersion, nameof(geometryVersion));
        sourceId = Required(sourceId, nameof(sourceId));
        sourceRevision = Required(sourceRevision, nameof(sourceRevision));
        attribution = Required(attribution, nameof(attribution));
        ArgumentNullException.ThrowIfNull(geometry);

        if (!Enum.IsDefined(typeof(ZoneKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind), "Unsupported zone kind.");

        if (geometry.IsEmpty)
            throw new ArgumentException("Zone geometry cannot be empty.", nameof(geometry));

        if (geometry is not Polygon and not MultiPolygon)
            throw new ArgumentException("Zone geometry must be a Polygon or MultiPolygon.", nameof(geometry));

        if (geometry.SRID != 4326)
            throw new ArgumentException("Zone geometry must use WGS84 SRID 4326.", nameof(geometry));

        foreach (var coordinate in geometry.Coordinates)
        {
            if (!double.IsFinite(coordinate.X) || coordinate.X is < -180 or > 180 ||
                !double.IsFinite(coordinate.Y) || coordinate.Y is < -90 or > 90)
                throw new ArgumentException(
                    "Zone geometry coordinates must be finite WGS84 longitude/latitude values.",
                    nameof(geometry));
        }

        var envelope = geometry.EnvelopeInternal;
        if (envelope.Width > 180)
            throw new ArgumentException(
                "Zone geometry cannot cross the antimeridian.",
                nameof(geometry));

        if (!geometry.IsValid)
            throw new ArgumentException(
                "Zone geometry must be topologically valid.",
                nameof(geometry));

        return new ZoneGeometryBoundary(
            zoneId, kind, geometryVersion, sourceId, sourceRevision, attribution, geometry);
    }

    public ZonePointRelation Classify(GeoCoordinate location)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!location.IsValid)
            throw new ArgumentException("A valid WGS84 coordinate is required.", nameof(location));

        var point = _geometry.Factory.CreatePoint(
            new Coordinate(location.Longitude, location.Latitude));

        if (_geometry.Boundary.Covers(point))
            return ZonePointRelation.Boundary;

        return _geometry.Contains(point)
            ? ZonePointRelation.Inside
            : ZonePointRelation.Outside;
    }

    private static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value is required.", parameterName);
        return value.Trim();
    }
}

public sealed record ZonePointResolution(
    ZonePointResolutionStatus Status,
    string? ZoneId,
    IReadOnlyList<string> CandidateZoneIds);

/// <summary>
/// Resolves a point against one hierarchy level only. Overlaps and shared borders
/// are reported explicitly rather than silently choosing an operating zone.
/// </summary>
public static class ZoneGeometryResolver
{
    public static ZonePointResolution Resolve(
        IReadOnlyCollection<ZoneGeometryBoundary> boundaries,
        ZoneKind kind,
        GeoCoordinate location)
    {
        ArgumentNullException.ThrowIfNull(boundaries);
        ArgumentNullException.ThrowIfNull(location);

        if (!Enum.IsDefined(typeof(ZoneKind), kind))
            throw new ArgumentOutOfRangeException(nameof(kind), "Unsupported zone kind.");
        if (!location.IsValid)
            throw new ArgumentException("A valid WGS84 coordinate is required.", nameof(location));
        if (boundaries.Any(boundary => boundary is null))
            throw new ArgumentException("Boundary collection cannot contain null entries.", nameof(boundaries));

        var relevant = boundaries.Where(boundary => boundary.Kind == kind).ToArray();
        var duplicateId = relevant.GroupBy(boundary => boundary.ZoneId, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateId is not null)
            throw new ArgumentException("Boundary zone IDs must be unique within a zone kind.", nameof(boundaries));

        var inside = new List<string>();
        var onBoundary = new List<string>();
        foreach (var boundary in relevant)
        {
            switch (boundary.Classify(location))
            {
                case ZonePointRelation.Inside:
                    inside.Add(boundary.ZoneId);
                    break;
                case ZonePointRelation.Boundary:
                    onBoundary.Add(boundary.ZoneId);
                    break;
            }
        }

        var candidates = inside.Concat(onBoundary).Order(StringComparer.Ordinal).ToArray();
        if (inside.Count == 1 && onBoundary.Count == 0)
            return new ZonePointResolution(ZonePointResolutionStatus.Matched, inside[0], candidates);
        if (inside.Count > 1 || onBoundary.Count > 1 || (inside.Count > 0 && onBoundary.Count > 0))
            return new ZonePointResolution(ZonePointResolutionStatus.Ambiguous, null, candidates);
        if (onBoundary.Count > 0)
            return new ZonePointResolution(ZonePointResolutionStatus.Boundary, null, candidates);

        return new ZonePointResolution(
            ZonePointResolutionStatus.Outside, null, Array.Empty<string>());
    }
}
