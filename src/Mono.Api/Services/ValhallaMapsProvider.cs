using System.Text.Json;
using Mono.Api.Domain;

namespace Mono.Api.Services;

/// <summary>
/// Calls a private Valhalla HTTP service for route and route-matrix estimates.
/// Geocoding and reverse geocoding intentionally remain unconfigured.
/// </summary>
public sealed class ValhallaMapsProvider(HttpClient httpClient) : IMapsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<GeocodeResult?> GeocodeAsync(string address, CancellationToken cancellationToken = default) =>
        Task.FromResult<GeocodeResult?>(null);

    public Task<string?> ReverseGeocodeAsync(
        GeoCoordinate location, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    public async Task<RouteEstimate?> GetRouteAsync(
        RouteQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var locations = new List<ValhallaLocation> { ToLocation(query.Origin) };
        locations.AddRange((query.Waypoints ?? []).Select(ToLocation));
        locations.Add(ToLocation(query.Destination));

        using var document = await SendAsync("route", new
        {
            locations,
            costing = GetCosting(query.Mode),
            units = "kilometers"
        }, cancellationToken);

        if (!document.RootElement.TryGetProperty("trip", out var trip) ||
            !trip.TryGetProperty("status", out var status) ||
            status.ValueKind != JsonValueKind.Number ||
            status.GetInt32() != 0)
            return null;

        if (!trip.TryGetProperty("summary", out var summary))
            throw new MapsProviderContractException("Valhalla route response did not include a summary.");

        var kilometers = ReadFiniteNumber(summary, "length");
        var seconds = ReadFiniteNumber(summary, "time");
        return new RouteEstimate(kilometers * 1000d, ToWholeSeconds(seconds));
    }

    public async Task<IReadOnlyList<IReadOnlyList<RouteMatrixCell>>> GetRouteMatrixAsync(
        RouteMatrixQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        using var document = await SendAsync("sources_to_targets", new
        {
            sources = query.Origins.Select(ToLocation).ToArray(),
            targets = query.Destinations.Select(ToLocation).ToArray(),
            costing = GetCosting(query.Mode),
            units = "kilometers",
            verbose = false
        }, cancellationToken);

        if (!document.RootElement.TryGetProperty("sources_to_targets", out var matrix) ||
            !matrix.TryGetProperty("durations", out var durations) ||
            !matrix.TryGetProperty("distances", out var distances) ||
            durations.ValueKind != JsonValueKind.Array ||
            distances.ValueKind != JsonValueKind.Array)
            throw new MapsProviderContractException("Valhalla matrix response did not include duration and distance arrays.");

        if (durations.GetArrayLength() != query.Origins.Count ||
            distances.GetArrayLength() != query.Origins.Count)
            throw new MapsProviderContractException("Valhalla matrix response has an invalid origin count.");

        var rows = new List<IReadOnlyList<RouteMatrixCell>>(query.Origins.Count);
        for (var rowIndex = 0; rowIndex < query.Origins.Count; rowIndex++)
        {
            var durationRow = durations[rowIndex];
            var distanceRow = distances[rowIndex];
            if (durationRow.ValueKind != JsonValueKind.Array ||
                distanceRow.ValueKind != JsonValueKind.Array ||
                durationRow.GetArrayLength() != query.Destinations.Count ||
                distanceRow.GetArrayLength() != query.Destinations.Count)
                throw new MapsProviderContractException("Valhalla matrix response has an invalid destination count.");

            var row = new List<RouteMatrixCell>(query.Destinations.Count);
            for (var columnIndex = 0; columnIndex < query.Destinations.Count; columnIndex++)
            {
                var duration = ReadNullableFiniteNumber(durationRow[columnIndex]);
                var distanceKilometers = ReadNullableFiniteNumber(distanceRow[columnIndex]);
                if ((duration is null) != (distanceKilometers is null))
                    throw new MapsProviderContractException("Valhalla matrix cells must include both estimates or neither.");

                row.Add(duration is null
                    ? new RouteMatrixCell(null, null)
                    : new RouteMatrixCell(distanceKilometers!.Value * 1000d, ToWholeSeconds(duration.Value)));
            }

            rows.Add(row);
        }

        return rows;
    }

    private async Task<JsonDocument> SendAsync(
        string service, object payload, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var uri = $"{service}?json={Uri.EscapeDataString(json)}";
        using var response = await httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new MapsProviderContractException($"Valhalla returned invalid JSON: {exception.Message}");
        }
    }

    private static ValhallaLocation ToLocation(GeoCoordinate location) =>
        new(location.Latitude, location.Longitude);

    private static string GetCosting(RouteTravelMode mode) => mode switch
    {
        RouteTravelMode.Walk => "pedestrian",
        RouteTravelMode.Bike => "bicycle",
        RouteTravelMode.Car => "auto",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unsupported route travel mode.")
    };

    private static double ReadFiniteNumber(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var number) ||
            !double.IsFinite(number) || number < 0)
            throw new MapsProviderContractException($"Valhalla returned an invalid '{propertyName}' estimate.");
        return number;
    }

    private static double? ReadNullableFiniteNumber(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var number) ||
            !double.IsFinite(number) || number < 0)
            throw new MapsProviderContractException("Valhalla returned an invalid matrix estimate.");
        return number;
    }

    private static int ToWholeSeconds(double seconds)
    {
        var rounded = Math.Ceiling(seconds);
        if (!double.IsFinite(rounded) || rounded > int.MaxValue)
            throw new MapsProviderContractException("Valhalla returned a duration outside the supported range.");
        return (int)rounded;
    }

    private sealed record ValhallaLocation(double Lat, double Lon);
}
