using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Operation.Union;

namespace Mono.Api.Services;

/// <summary>
/// Compares an explicitly selected set of child boundaries with one parent boundary.
/// The report is advisory and does not imply that uncovered areas must be served.
/// </summary>
public sealed class MonoZoneCoverageCompletenessService(
    PostgresZoneGeometryRepository geometryRepository,
    IMonoOperationsContext operationsContext)
{
    private const int MaximumChildren = 50;
    private const int MaximumCoordinates = 100_000;

    public async Task<ZoneCoverageCompletenessReport> CheckAsync(
        ZoneGeometrySnapshotReference? parent,
        IReadOnlyList<ZoneGeometrySnapshotReference>? children,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();

        if (parent is null || string.IsNullOrWhiteSpace(parent.ZoneId) ||
            string.IsNullOrWhiteSpace(parent.GeometryVersion))
            throw new ArgumentException("A valid parent snapshot is required.", nameof(parent));
        if (children is null || children.Count is < 1 or > MaximumChildren)
            throw new ArgumentException(
                $"Between 1 and {MaximumChildren} child snapshots are required.", nameof(children));
        if (children.Any(item => item is null ||
                string.IsNullOrWhiteSpace(item.ZoneId) ||
                string.IsNullOrWhiteSpace(item.GeometryVersion)))
            throw new ArgumentException("Every child snapshot requires a zone ID and geometry version.", nameof(children));

        var parentReference = Normalize(parent);
        var childReferences = children.Select(Normalize).ToArray();
        if (childReferences.Any(item => item.ZoneId == parentReference.ZoneId) ||
            childReferences.Select(item => item.ZoneId).Distinct(StringComparer.Ordinal).Count() != childReferences.Length)
            throw new ArgumentException("Parent and child zone IDs must be unique.", nameof(children));

        var parentBoundary = await geometryRepository.FindAsync(
            parentReference.ZoneId, parentReference.GeometryVersion, cancellationToken);
        if (parentBoundary is null)
            throw new KeyNotFoundException(
                $"Parent snapshot {parentReference.ZoneId}/{parentReference.GeometryVersion} was not found.");

        var childKind = parentBoundary.Kind switch
        {
            ZoneKind.City => ZoneKind.OperatingZone,
            ZoneKind.OperatingZone => ZoneKind.MicroZone,
            _ => throw new ArgumentException("Coverage completeness supports City or OperatingZone parents.")
        };

        var parentGeometry = ReadGeometry(parentBoundary);
        var coordinateCount = parentGeometry.NumPoints;
        if (coordinateCount > MaximumCoordinates)
            throw CoordinateLimitExceeded();

        var childGeometries = new List<Geometry>(childReferences.Length);
        var outsideChildren = new List<ZoneGeometrySnapshotReference>();
        foreach (var reference in childReferences)
        {
            var boundary = await geometryRepository.FindAsync(
                reference.ZoneId, reference.GeometryVersion, cancellationToken);
            if (boundary is null)
                throw new KeyNotFoundException(
                    $"Child snapshot {reference.ZoneId}/{reference.GeometryVersion} was not found.");
            if (boundary.Kind != childKind)
                throw new ArgumentException(
                    $"A {parentBoundary.Kind} parent requires {childKind} child snapshots.", nameof(children));

            var geometry = ReadGeometry(boundary);
            coordinateCount += geometry.NumPoints;
            if (coordinateCount > MaximumCoordinates)
                throw CoordinateLimitExceeded();
            childGeometries.Add(geometry);
            if (!parentGeometry.Covers(geometry))
                outsideChildren.Add(reference);
        }

        var childUnion = UnaryUnionOp.Union(childGeometries);
        var uncovered = parentGeometry.Difference(childUnion);
        var gaps = GetPolygonParts(uncovered)
            .Select(polygon =>
            {
                var envelope = polygon.EnvelopeInternal;
                return new ZoneCoverageGapEnvelope(
                    envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
            })
            .ToArray();

        var noGaps = uncovered.IsEmpty;
        var allChildrenWithinParent = outsideChildren.Count == 0;
        return new ZoneCoverageCompletenessReport(
            parentReference, parentBoundary.Kind.ToString(), childKind.ToString(),
            childReferences.Length, coordinateCount, noGaps, allChildrenWithinParent,
            noGaps && allChildrenWithinParent, gaps, outsideChildren);
    }

    private static ZoneGeometrySnapshotReference Normalize(ZoneGeometrySnapshotReference reference) =>
        new(reference.ZoneId.Trim(), reference.GeometryVersion.Trim());

    private static Geometry ReadGeometry(ZoneGeometryBoundary boundary)
    {
        var geometry = new WKBReader().Read(boundary.ToWellKnownBinary());
        geometry.SRID = 4326;
        return geometry;
    }

    private static IEnumerable<Polygon> GetPolygonParts(Geometry geometry)
    {
        if (geometry.IsEmpty)
            yield break;
        if (geometry is Polygon polygon)
        {
            yield return polygon;
            yield break;
        }

        for (var index = 0; index < geometry.NumGeometries; index++)
        {
            foreach (var part in GetPolygonParts(geometry.GetGeometryN(index)))
                yield return part;
        }
    }

    private void RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
    }

    private static ArgumentException CoordinateLimitExceeded() =>
        new($"The selected snapshots exceed the {MaximumCoordinates} coordinate limit.");

    public sealed record ZoneCoverageGapEnvelope(
        double MinLongitude,
        double MinLatitude,
        double MaxLongitude,
        double MaxLatitude);
}

public sealed record ZoneCoverageCompletenessReport(
    ZoneGeometrySnapshotReference Parent,
    string ParentKind,
    string ChildKind,
    int ChildCount,
    int CoordinateCount,
    bool HasNoGaps,
    bool AllChildrenWithinParent,
    bool Passed,
    IReadOnlyList<MonoZoneCoverageCompletenessService.ZoneCoverageGapEnvelope> GapEnvelopes,
    IReadOnlyList<ZoneGeometrySnapshotReference> ChildrenOutsideParent);
