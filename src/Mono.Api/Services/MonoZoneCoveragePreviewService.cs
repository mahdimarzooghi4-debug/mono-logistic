using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Services;

/// <summary>
/// Internal diagnostic preview against currently active zone snapshots.
/// Results are advisory and do not authorize delivery or select ambiguous zones.
/// </summary>
public sealed class MonoZoneCoveragePreviewService(
    PostgresZoneGeometryRepository geometryRepository,
    IMonoOperationsContext operationsContext)
{
    public async Task<ZoneCoveragePreviewResponse> PreviewAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        RequireAdministrator();
        var coordinate = new GeoCoordinate(latitude, longitude);
        if (!coordinate.IsValid)
            throw new ArgumentException("Valid WGS84 latitude and longitude are required.");

        var active = await geometryRepository.ListActiveAsync(cancellationToken);
        var layers = Enum.GetValues<ZoneKind>()
            .Select(kind =>
            {
                var boundaries = active.Where(boundary => boundary.Kind == kind).ToArray();
                if (boundaries.Length == 0)
                    return new ZoneCoverageLayerPreview(
                        kind, false, null, null, Array.Empty<string>());

                var resolution = ZoneGeometryResolver.Resolve(boundaries, kind, coordinate);
                return new ZoneCoverageLayerPreview(
                    kind, true, resolution.Status, resolution.ZoneId, resolution.CandidateZoneIds);
            })
            .ToArray();

        return new ZoneCoveragePreviewResponse(
            latitude, longitude, active.Count, active.Count > 0, layers);
    }

    private void RequireAdministrator()
    {
        if (string.IsNullOrWhiteSpace(operationsContext.ActorId) ||
            !operationsContext.Roles.Contains(MonoOperationsRoles.Administrator, StringComparer.Ordinal))
            throw new UnauthorizedAccessException("An authenticated Mono administrator is required.");
    }
}

public sealed record ZoneCoverageLayerPreview(
    ZoneKind Kind,
    bool HasActiveBoundaries,
    ZonePointResolutionStatus? Status,
    string? ZoneId,
    IReadOnlyList<string> CandidateZoneIds);

public sealed record ZoneCoveragePreviewResponse(
    double Latitude,
    double Longitude,
    int ActiveBoundaryCount,
    bool HasAnyActiveBoundaries,
    IReadOnlyList<ZoneCoverageLayerPreview> Layers);
