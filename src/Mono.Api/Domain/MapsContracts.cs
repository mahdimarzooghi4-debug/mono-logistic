namespace Mono.Api.Domain;

/// <summary>A WGS84 coordinate in decimal degrees.</summary>
public sealed record GeoCoordinate(double Latitude, double Longitude)
{
    public bool IsValid =>
        double.IsFinite(Latitude) && Latitude is >= -90 and <= 90 &&
        double.IsFinite(Longitude) && Longitude is >= -180 and <= 180;
}

/// <summary>Coordinates resolved for an address, when a provider finds a match.</summary>
public sealed record GeocodeResult(GeoCoordinate Location, string? FormattedAddress);

/// <summary>A route query between two locations with optional intermediate waypoints.</summary>
public sealed record RouteQuery(
    GeoCoordinate Origin,
    GeoCoordinate Destination,
    IReadOnlyList<GeoCoordinate>? Waypoints = null);

/// <summary>Provider route estimate. Distance is in metres; duration is in whole seconds.</summary>
public sealed record RouteEstimate(double DistanceMeters, int EstimatedDurationSeconds);

/// <summary>A matrix query; each returned row corresponds to an origin and each cell to a destination.</summary>
public sealed record RouteMatrixQuery(
    IReadOnlyList<GeoCoordinate> Origins,
    IReadOnlyList<GeoCoordinate> Destinations);

/// <summary>A matrix cell. Both values are null when no route is available.</summary>
public sealed record RouteMatrixCell(double? DistanceMeters, int? EstimatedDurationSeconds);
