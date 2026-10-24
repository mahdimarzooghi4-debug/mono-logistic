using Mono.Api.Domain;

namespace Mono.Api.Services;

/// <summary>Resolves stable route location IDs without coupling route plans to a maps vendor.</summary>
public interface IRouteLocationCoordinateResolver
{
    Task<IReadOnlyDictionary<string, GeoCoordinate>> ResolveAsync(
        IReadOnlyList<string> locationIds,
        CancellationToken cancellationToken = default);
}

public interface IRouteLocationCoordinateCatalog : IRouteLocationCoordinateResolver
{
    Task<RouteLocationCoordinateWriteResult> WriteAsync(
        RouteLocationCoordinateWriteCommand command,
        CancellationToken cancellationToken = default);

    Task<RouteLocationCoordinateVersion?> FindVersionAsync(
        string locationId,
        int version,
        CancellationToken cancellationToken = default);
}

public sealed record RouteMatrixAcquisitionRequest(
    string RouteId,
    int RouteVersion,
    RoutePlanSnapshot ExistingPlan,
    RouteInsertionMission Candidate,
    RouteTravelMode TravelMode,
    string SourceId,
    string EvidenceId,
    DateTimeOffset CapturedAt);

/// <summary>
/// Obtains a directed matrix for the exact route location IDs. The returned evidence is
/// unsigned; a configured trusted source must attest to it before planning can use it.
/// </summary>
public sealed class RouteMatrixAcquisitionService(
    IMapsGateway maps,
    IRouteLocationCoordinateResolver coordinates)
{
    public async Task<RouteTravelTimeEvidence> AcquireAsync(
        RouteMatrixAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RouteId) || request.RouteVersion <= 0 ||
            request.ExistingPlan is null || request.Candidate is null ||
            request.ExistingPlan.RouteId != request.RouteId || !Enum.IsDefined(request.TravelMode) ||
            string.IsNullOrWhiteSpace(request.SourceId) || string.IsNullOrWhiteSpace(request.EvidenceId) ||
            request.CapturedAt == default || request.CapturedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Route matrix acquisition request is malformed.", nameof(request));

        RoutePlanSnapshotValidator.Validate(new RoutePlanVersionWriteCommand(
            "matrix-acquisition-validation", request.RouteVersion, request.ExistingPlan));

        var locationIds = request.ExistingPlan.OrderedStops
            .Select(stop => stop.LocationId)
            .Append(request.ExistingPlan.StartLocationId)
            .Append(request.Candidate.PickupLocationId)
            .Append(request.Candidate.DropoffLocationId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();
        var resolved = await coordinates.ResolveAsync(locationIds, cancellationToken);
        if (resolved is null || resolved.Count != locationIds.Length ||
            locationIds.Any(id => !resolved.TryGetValue(id, out var coordinate) ||
                                  coordinate is null || !coordinate.IsValid))
            throw new RouteLocationResolutionException("Every route location must resolve to one valid coordinate.");

        var providerMatrix = await maps.GetRouteMatrixAsync(new RouteMatrixQuery(
            locationIds.Select(id => resolved[id]).ToArray(),
            locationIds.Select(id => resolved[id]).ToArray(),
            request.TravelMode), cancellationToken);

        var byLocation = new Dictionary<string, IReadOnlyDictionary<string, int?>>(StringComparer.Ordinal);
        for (var row = 0; row < locationIds.Length; row++)
        {
            var cells = new Dictionary<string, int?>(StringComparer.Ordinal);
            for (var column = 0; column < locationIds.Length; column++)
                cells.Add(locationIds[column], providerMatrix[row][column].EstimatedDurationSeconds);
            byLocation.Add(locationIds[row], cells);
        }

        return new RouteTravelTimeEvidence(
            request.SourceId,
            request.EvidenceId,
            request.CapturedAt,
            string.Empty,
            new RouteTravelTimeMatrix(true, false, byLocation),
            request.TravelMode,
            resolved.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }
}

public sealed class RouteLocationResolutionException(string message) : InvalidOperationException(message);
