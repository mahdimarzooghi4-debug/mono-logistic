using Mono.Api.Domain;

namespace Mono.Api.Services;

public sealed record InitialRouteMatrixAcquisitionRequest(
    string RouteId,
    string StartLocationId,
    DateTimeOffset StartedAt,
    RouteInsertionMission Mission,
    RouteTravelMode TravelMode,
    string SourceId,
    string EvidenceId,
    DateTimeOffset CapturedAt);

/// <summary>Acquires the directed start/pickup/drop-off matrix for a proposed first Route version.</summary>
public sealed class InitialRouteMatrixAcquisitionService(
    IMapsGateway maps,
    IRouteLocationCoordinateResolver coordinates)
{
    public async Task<RouteTravelTimeEvidence> AcquireAsync(
        InitialRouteMatrixAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RouteId) || string.IsNullOrWhiteSpace(request.StartLocationId) ||
            request.StartedAt == default || request.StartedAt.Offset != TimeSpan.Zero || request.Mission is null ||
            !Enum.IsDefined(request.TravelMode) || string.IsNullOrWhiteSpace(request.SourceId) ||
            string.IsNullOrWhiteSpace(request.EvidenceId) || request.CapturedAt == default ||
            request.CapturedAt.Offset != TimeSpan.Zero)
            throw new ArgumentException("Initial route matrix acquisition request is malformed.", nameof(request));

        var ids = new[] { request.StartLocationId, request.Mission.PickupLocationId,
                request.Mission.DropoffLocationId }.Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var resolved = await coordinates.ResolveAsync(ids, cancellationToken);
        if (resolved is null || resolved.Count != ids.Length ||
            ids.Any(id => !resolved.TryGetValue(id, out var coordinate) || coordinate is null || !coordinate.IsValid))
            throw new RouteLocationResolutionException("Every initial route location must resolve to one valid coordinate.");

        var matrix = await maps.GetRouteMatrixAsync(new RouteMatrixQuery(
            ids.Select(id => resolved[id]).ToArray(), ids.Select(id => resolved[id]).ToArray(), request.TravelMode),
            cancellationToken);
        var cells = new Dictionary<string, IReadOnlyDictionary<string, int?>>(StringComparer.Ordinal);
        for (var row = 0; row < ids.Length; row++)
        {
            var values = new Dictionary<string, int?>(StringComparer.Ordinal);
            for (var column = 0; column < ids.Length; column++)
                values.Add(ids[column], matrix[row][column].EstimatedDurationSeconds);
            cells.Add(ids[row], values);
        }

        return new RouteTravelTimeEvidence(
            request.SourceId, request.EvidenceId, request.CapturedAt, string.Empty,
            new RouteTravelTimeMatrix(true, false, cells), request.TravelMode,
            resolved.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }
}
