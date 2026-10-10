using Mono.Api.Domain;

namespace Mono.Api.Services;

/// <summary>Provider contract for geocoding, reverse geocoding, routes, ETAs, and route matrices.</summary>
public interface IMapsProvider
{
    Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken cancellationToken = default);
    Task<string?> ReverseGeocodeAsync(GeoCoordinate location, CancellationToken cancellationToken = default);
    Task<RouteEstimate?> GetRouteAsync(RouteQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IReadOnlyList<RouteMatrixCell>>> GetRouteMatrixAsync(
        RouteMatrixQuery query, CancellationToken cancellationToken = default);
}

/// <summary>Provider-neutral application contract for maps operations.</summary>
public interface IMapsGateway
{
    Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken cancellationToken = default);
    Task<string?> ReverseGeocodeAsync(GeoCoordinate location, CancellationToken cancellationToken = default);
    Task<RouteEstimate?> GetRouteAsync(RouteQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IReadOnlyList<RouteMatrixCell>>> GetRouteMatrixAsync(
        RouteMatrixQuery query, CancellationToken cancellationToken = default);
}

/// <summary>Validates inputs and provider responses without selecting a concrete maps vendor.</summary>
public sealed class MapsGateway(IMapsProvider provider) : IMapsGateway
{
    public async Task<GeocodeResult?> GeocodeAsync(
        string address, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        var result = await provider.GeocodeAsync(address.Trim(), cancellationToken);
        if (result is null)
            return null;
        if (result.Location is null || !result.Location.IsValid)
            throw new MapsProviderContractException("Geocoding provider returned an invalid coordinate.");
        return result with
        {
            FormattedAddress = string.IsNullOrWhiteSpace(result.FormattedAddress)
                ? null
                : result.FormattedAddress.Trim()
        };
    }

    public async Task<string?> ReverseGeocodeAsync(
        GeoCoordinate location, CancellationToken cancellationToken = default)
    {
        EnsureCoordinate(location, "A valid WGS84 coordinate is required.");
        var address = await provider.ReverseGeocodeAsync(location, cancellationToken);
        return string.IsNullOrWhiteSpace(address) ? null : address.Trim();
    }

    public async Task<RouteEstimate?> GetRouteAsync(
        RouteQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureCoordinate(query.Origin, "Route origin must be a valid WGS84 coordinate.");
        EnsureCoordinate(query.Destination, "Route destination must be a valid WGS84 coordinate.");
        foreach (var waypoint in query.Waypoints ?? [])
            EnsureCoordinate(waypoint, "Every route waypoint must be a valid WGS84 coordinate.");

        var estimate = await provider.GetRouteAsync(query, cancellationToken);
        if (estimate is not null)
            EnsureEstimate(estimate);
        return estimate;
    }

    public async Task<IReadOnlyList<IReadOnlyList<RouteMatrixCell>>> GetRouteMatrixAsync(
        RouteMatrixQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Origins is null || query.Origins.Count == 0 ||
            query.Destinations is null || query.Destinations.Count == 0)
            throw new ArgumentException("Route matrix needs at least one origin and one destination.", nameof(query));

        foreach (var origin in query.Origins)
            EnsureCoordinate(origin, "Every route matrix origin must be a valid WGS84 coordinate.");
        foreach (var destination in query.Destinations)
            EnsureCoordinate(destination, "Every route matrix destination must be a valid WGS84 coordinate.");

        var matrix = await provider.GetRouteMatrixAsync(query, cancellationToken);
        if (matrix is null || matrix.Count != query.Origins.Count ||
            matrix.Any(row => row is null || row.Count != query.Destinations.Count))
            throw new MapsProviderContractException("Maps provider returned a route matrix with invalid dimensions.");

        foreach (var cell in matrix.SelectMany(row => row))
        {
            if (cell is null)
                throw new MapsProviderContractException("Maps provider returned an empty route matrix cell.");
            if ((cell.DistanceMeters is null) != (cell.EstimatedDurationSeconds is null))
                throw new MapsProviderContractException("Route matrix cells must include both estimate values or neither.");
            if (cell.DistanceMeters is { } distance &&
                (!double.IsFinite(distance) || distance < 0))
                throw new MapsProviderContractException("Maps provider returned an invalid route matrix estimate.");
            if (cell.EstimatedDurationSeconds is { } duration && duration < 0)
                throw new MapsProviderContractException("Maps provider returned an invalid route matrix estimate.");
        }

        return matrix;
    }

    private static void EnsureCoordinate(GeoCoordinate coordinate, string message)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        if (!coordinate.IsValid)
            throw new ArgumentException(message);
    }

    private static void EnsureEstimate(RouteEstimate estimate)
    {
        if (!double.IsFinite(estimate.DistanceMeters) || estimate.DistanceMeters < 0 ||
            estimate.EstimatedDurationSeconds < 0)
            throw new MapsProviderContractException("Maps provider returned an invalid route estimate.");
    }
}

public sealed class MapsProviderContractException(string message) : InvalidOperationException(message);
