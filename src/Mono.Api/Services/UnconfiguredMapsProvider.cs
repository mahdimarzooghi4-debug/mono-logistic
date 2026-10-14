using Mono.Api.Domain;

namespace Mono.Api.Services;

/// <summary>Safe default until Mono selects and configures a routing/geocoding provider.</summary>
public sealed class UnconfiguredMapsProvider : IMapsProvider
{
    public Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken cancellationToken = default) =>
        Task.FromResult<GeocodeResult?>(null);

    public Task<string?> ReverseGeocodeAsync(GeoCoordinate location, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public Task<RouteEstimate?> GetRouteAsync(RouteQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult<RouteEstimate?>(null);

    public Task<IReadOnlyList<IReadOnlyList<RouteMatrixCell>>> GetRouteMatrixAsync(
        RouteMatrixQuery query,
        CancellationToken cancellationToken = default) =>
        throw new MapsProviderNotConfiguredException();
}

public sealed class MapsProviderNotConfiguredException()
    : InvalidOperationException("A maps provider has not been configured.");
