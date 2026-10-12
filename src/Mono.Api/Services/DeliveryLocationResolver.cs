using Mono.Api.Domain;

namespace Mono.Api.Services;

public enum LocationResolutionSource
{
    ProvidedCoordinates,
    GeocodedAddress
}

public sealed record ResolvedDeliveryLocation(
    GeoCoordinate Location,
    string? AddressText,
    LocationResolutionSource Source);

/// <summary>Resolves delivery locations without binding callers to a maps provider.</summary>
public sealed class DeliveryLocationResolver(IMapsGateway mapsGateway)
{
    public async Task<ResolvedDeliveryLocation?> ResolveAsync(
        DeliveryLocationInput location,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(location);

        var hasLatitude = location.Latitude.HasValue;
        var hasLongitude = location.Longitude.HasValue;
        if (hasLatitude != hasLongitude)
            throw new ArgumentException(
                "Latitude and longitude must be provided together.",
                nameof(location));

        var address = string.IsNullOrWhiteSpace(location.AddressText)
            ? null
            : location.AddressText.Trim();

        if (hasLatitude)
        {
            var coordinate = new GeoCoordinate(
                (double)location.Latitude!.Value,
                (double)location.Longitude!.Value);
            if (!coordinate.IsValid)
                throw new ArgumentException(
                    "A valid WGS84 coordinate pair is required.",
                    nameof(location));

            return new ResolvedDeliveryLocation(
                coordinate,
                address,
                LocationResolutionSource.ProvidedCoordinates);
        }

        if (address is null)
            throw new ArgumentException(
                "An address or a coordinate pair is required.",
                nameof(location));

        var geocode = await mapsGateway.GeocodeAsync(address, cancellationToken);
        if (geocode is null)
            return null;

        return new ResolvedDeliveryLocation(
            geocode.Location,
            string.IsNullOrWhiteSpace(geocode.FormattedAddress)
                ? address
                : geocode.FormattedAddress.Trim(),
            LocationResolutionSource.GeocodedAddress);
    }
}
