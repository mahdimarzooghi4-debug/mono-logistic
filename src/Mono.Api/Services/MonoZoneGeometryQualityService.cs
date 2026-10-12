using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace Mono.Api.Services;

/// <summary>
/// Runs an advisory same-layer overlap check on caller-selected immutable snapshots.
/// It never changes reviews, activation, or delivery decisions.
/// </summary>
public sealed class MonoZoneGeometryQualityService(
    PostgresZoneGeometryRepository geometryRepository,
    IMonoOperationsContext operationsContext)
{
    private const int MaximumSnapshots = 50;
    private const int MaximumCoordinates = 100_000;

    public async Task<ZoneGeometryQualityReport> CheckAsync(
        string kind,
        IReadOnlyList<ZoneGeometrySnapshotReference>? snapshots,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();

        if (!Enum.TryParse<ZoneKind>(kind, ignoreCase: false, out var zoneKind) ||
            !Enum.IsDefined(typeof(ZoneKind), zoneKind))
            throw new ArgumentException("Unsupported zone kind.", nameof(kind));
        if (snapshots is null || snapshots.Count is < 2 or > MaximumSnapshots)
            throw new ArgumentException(
                $"Between 2 and {MaximumSnapshots} snapshots are required.", nameof(snapshots));
        if (snapshots.Any(item => item is null ||
                string.IsNullOrWhiteSpace(item.ZoneId) ||
                string.IsNullOrWhiteSpace(item.GeometryVersion)))
            throw new ArgumentException("Every snapshot requires a zone ID and geometry version.", nameof(snapshots));

        var normalized = snapshots.Select(item => new ZoneGeometrySnapshotReference(
                item.ZoneId.Trim(), item.GeometryVersion.Trim()))
            .ToArray();
        if (normalized.Select(item => item.ZoneId).Distinct(StringComparer.Ordinal).Count() != normalized.Length)
            throw new ArgumentException("A zone ID may appear only once in a quality check.", nameof(snapshots));

        var checkedSnapshots = new List<CheckedSnapshot>(normalized.Length);
        var coordinateCount = 0;
        foreach (var reference in normalized)
        {
            var boundary = await geometryRepository.FindAsync(
                reference.ZoneId, reference.GeometryVersion, cancellationToken);
            if (boundary is null)
                throw new KeyNotFoundException(
                    $"Zone geometry snapshot {reference.ZoneId}/{reference.GeometryVersion} was not found.");
            if (boundary.Kind != zoneKind)
                throw new ArgumentException("All snapshots must match the requested zone kind.", nameof(snapshots));

            var geometry = new WKBReader().Read(boundary.ToWellKnownBinary());
            geometry.SRID = 4326;
            coordinateCount += geometry.NumPoints;
            if (coordinateCount > MaximumCoordinates)
                throw new ArgumentException(
                    $"The selected snapshots exceed the {MaximumCoordinates} coordinate limit.", nameof(snapshots));

            checkedSnapshots.Add(new CheckedSnapshot(reference, geometry));
        }

        var overlaps = new List<ZoneGeometryOverlap>();
        for (var leftIndex = 0; leftIndex < checkedSnapshots.Count; leftIndex++)
        {
            var left = checkedSnapshots[leftIndex];
            for (var rightIndex = leftIndex + 1; rightIndex < checkedSnapshots.Count; rightIndex++)
            {
                var right = checkedSnapshots[rightIndex];
                if (!left.Geometry.EnvelopeInternal.Intersects(right.Geometry.EnvelopeInternal))
                    continue;

                if (left.Geometry.Relate(right.Geometry).Matches("2********"))
                    overlaps.Add(new ZoneGeometryOverlap(
                        left.Reference, right.Reference, "InteriorOverlap"));
            }
        }

        return new ZoneGeometryQualityReport(
            zoneKind.ToString(), checkedSnapshots.Count, coordinateCount,
            overlaps.Count == 0, overlaps);
    }

    public async Task<ZoneGeometryContainmentReport> CheckContainmentAsync(
        string parentKind,
        ZoneGeometrySnapshotReference? parent,
        string childKind,
        IReadOnlyList<ZoneGeometrySnapshotReference>? children,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();

        if (!Enum.TryParse<ZoneKind>(parentKind, ignoreCase: false, out var parsedParentKind) ||
            !Enum.IsDefined(typeof(ZoneKind), parsedParentKind))
            throw new ArgumentException("Unsupported parent zone kind.", nameof(parentKind));
        if (!Enum.TryParse<ZoneKind>(childKind, ignoreCase: false, out var parsedChildKind) ||
            !Enum.IsDefined(typeof(ZoneKind), parsedChildKind))
            throw new ArgumentException("Unsupported child zone kind.", nameof(childKind));

        var validHierarchy =
            (parsedParentKind == ZoneKind.City && parsedChildKind == ZoneKind.OperatingZone) ||
            (parsedParentKind == ZoneKind.OperatingZone && parsedChildKind == ZoneKind.MicroZone);
        if (!validHierarchy)
            throw new ArgumentException("Only City to OperatingZone and OperatingZone to MicroZone checks are supported.");
        if (parent is null || string.IsNullOrWhiteSpace(parent.ZoneId) ||
            string.IsNullOrWhiteSpace(parent.GeometryVersion))
            throw new ArgumentException("A valid parent snapshot is required.", nameof(parent));
        if (children is null || children.Count is < 1 or > MaximumSnapshots)
            throw new ArgumentException(
                $"Between 1 and {MaximumSnapshots} child snapshots are required.", nameof(children));
        if (children.Any(item => item is null ||
                string.IsNullOrWhiteSpace(item.ZoneId) ||
                string.IsNullOrWhiteSpace(item.GeometryVersion)))
            throw new ArgumentException("Every child snapshot requires a zone ID and geometry version.", nameof(children));

        var normalizedParent = new ZoneGeometrySnapshotReference(
            parent.ZoneId.Trim(), parent.GeometryVersion.Trim());
        var normalizedChildren = children.Select(item => new ZoneGeometrySnapshotReference(
                item.ZoneId.Trim(), item.GeometryVersion.Trim()))
            .ToArray();
        if (normalizedChildren.Any(item => item.ZoneId == normalizedParent.ZoneId) ||
            normalizedChildren.Select(item => item.ZoneId).Distinct(StringComparer.Ordinal).Count() != normalizedChildren.Length)
            throw new ArgumentException("Parent and child zone IDs must be unique in a containment check.", nameof(children));

        var parentBoundary = await geometryRepository.FindAsync(
            normalizedParent.ZoneId, normalizedParent.GeometryVersion, cancellationToken);
        if (parentBoundary is null)
            throw new KeyNotFoundException(
                $"Parent snapshot {normalizedParent.ZoneId}/{normalizedParent.GeometryVersion} was not found.");
        if (parentBoundary.Kind != parsedParentKind)
            throw new ArgumentException("Parent snapshot kind does not match parentKind.", nameof(parent));

        var parentGeometry = new WKBReader().Read(parentBoundary.ToWellKnownBinary());
        parentGeometry.SRID = 4326;
        var coordinateCount = parentGeometry.NumPoints;
        if (coordinateCount > MaximumCoordinates)
            throw new ArgumentException(
                $"The selected snapshots exceed the {MaximumCoordinates} coordinate limit.", nameof(children));

        var checkedChildren = new List<CheckedSnapshot>(normalizedChildren.Length);
        foreach (var reference in normalizedChildren)
        {
            var boundary = await geometryRepository.FindAsync(
                reference.ZoneId, reference.GeometryVersion, cancellationToken);
            if (boundary is null)
                throw new KeyNotFoundException(
                    $"Child snapshot {reference.ZoneId}/{reference.GeometryVersion} was not found.");
            if (boundary.Kind != parsedChildKind)
                throw new ArgumentException("A child snapshot kind does not match childKind.", nameof(children));

            var geometry = new WKBReader().Read(boundary.ToWellKnownBinary());
            geometry.SRID = 4326;
            coordinateCount += geometry.NumPoints;
            if (coordinateCount > MaximumCoordinates)
                throw new ArgumentException(
                    $"The selected snapshots exceed the {MaximumCoordinates} coordinate limit.", nameof(children));
            checkedChildren.Add(new CheckedSnapshot(reference, geometry));
        }

        var findings = checkedChildren
            .Where(child => !parentGeometry.Covers(child.Geometry))
            .Select(child => new ZoneGeometryContainmentFinding(
                child.Reference, normalizedParent, "OutsideParent"))
            .ToArray();

        return new ZoneGeometryContainmentReport(
            parsedParentKind.ToString(), normalizedParent,
            parsedChildKind.ToString(), checkedChildren.Count, coordinateCount,
            findings.Length == 0, findings);
    }

    private void RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
    }

    private sealed record CheckedSnapshot(
        ZoneGeometrySnapshotReference Reference,
        Geometry Geometry);
}

public sealed record ZoneGeometrySnapshotReference(string ZoneId, string GeometryVersion);

public sealed record ZoneGeometryOverlap(
    ZoneGeometrySnapshotReference First,
    ZoneGeometrySnapshotReference Second,
    string Issue);

public sealed record ZoneGeometryQualityReport(
    string Kind,
    int SnapshotCount,
    int CoordinateCount,
    bool Passed,
    IReadOnlyList<ZoneGeometryOverlap> Findings);

public sealed record ZoneGeometryContainmentFinding(
    ZoneGeometrySnapshotReference Child,
    ZoneGeometrySnapshotReference Parent,
    string Issue);

public sealed record ZoneGeometryContainmentReport(
    string ParentKind,
    ZoneGeometrySnapshotReference Parent,
    string ChildKind,
    int ChildCount,
    int CoordinateCount,
    bool Passed,
    IReadOnlyList<ZoneGeometryContainmentFinding> Findings);
