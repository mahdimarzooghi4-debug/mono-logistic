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
