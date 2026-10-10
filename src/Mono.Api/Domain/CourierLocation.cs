using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record CourierLocationUpdate
{
    [JsonPropertyName("courier_id")]
    public string CourierId { get; init; } = string.Empty;

    [JsonPropertyName("latitude")]
    public double Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; init; }

    [JsonPropertyName("accuracy_meters")]
    public double? AccuracyMeters { get; init; }

    [JsonPropertyName("heading_degrees")]
    public double? HeadingDegrees { get; init; }

    [JsonPropertyName("speed_meters_per_second")]
    public double? SpeedMetersPerSecond { get; init; }

    [JsonPropertyName("observed_at")]
    public DateTimeOffset? ObservedAt { get; init; }

    [JsonPropertyName("active_route_id")]
    public string? ActiveRouteId { get; init; }

    public CourierLocationUpdate Normalize() => this with
    {
        CourierId = CourierId?.Trim() ?? string.Empty,
        ActiveRouteId = string.IsNullOrWhiteSpace(ActiveRouteId)
            ? null
            : ActiveRouteId.Trim(),
        ObservedAt = ObservedAt?.ToUniversalTime()
    };
}

public static class CourierLocationUpdateValidator
{
    public static IReadOnlyList<string> Validate(CourierLocationUpdate input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var normalized = input.Normalize();
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(normalized.CourierId))
            errors.Add("courier_id is required.");

        if (!new GeoCoordinate(normalized.Latitude, normalized.Longitude).IsValid)
            errors.Add("latitude and longitude must be finite WGS84 coordinates.");

        if (normalized.AccuracyMeters is { } accuracy &&
            (!double.IsFinite(accuracy) || accuracy < 0))
            errors.Add("accuracy_meters must be finite and non-negative when provided.");

        if (normalized.HeadingDegrees is { } heading &&
            (!double.IsFinite(heading) || heading < 0 || heading >= 360))
            errors.Add("heading_degrees must be finite and from 0 inclusive to 360 exclusive when provided.");

        if (normalized.SpeedMetersPerSecond is { } speed &&
            (!double.IsFinite(speed) || speed < 0))
            errors.Add("speed_meters_per_second must be finite and non-negative when provided.");

        if (normalized.ObservedAt is null)
            errors.Add("observed_at is required.");

        return errors;
    }
}

public sealed record CourierCurrentPosition(
    string CourierId,
    GeoCoordinate Location,
    double? AccuracyMeters,
    double? HeadingDegrees,
    double? SpeedMetersPerSecond,
    DateTimeOffset ObservedAt,
    string? ActiveRouteId)
{
    /// <summary>Evaluates freshness using an explicit caller-supplied age limit.</summary>
    public bool IsFreshAt(DateTimeOffset now, TimeSpan maximumAge)
    {
        if (maximumAge < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maximumAge));

        var currentTime = now.ToUniversalTime();
        var observedTime = ObservedAt.ToUniversalTime();
        return observedTime <= currentTime &&
               currentTime - observedTime <= maximumAge;
    }
}

public sealed record CourierLocationProjectionResult(
    CourierCurrentPosition Position,
    bool Applied);

public static class CourierLocationProjector
{
    /// <summary>
    /// Advances one courier's current-position projection only for a strictly newer event.
    /// The caller owns persistence, identity authorization, and synchronization.
    /// </summary>
    public static CourierLocationProjectionResult Apply(
        CourierCurrentPosition? current,
        CourierLocationUpdate update)
    {
        var errors = CourierLocationUpdateValidator.Validate(update);
        if (errors.Count > 0)
            throw new ArgumentException(
                "Invalid courier location update: " + string.Join(" ", errors),
                nameof(update));

        var normalized = update.Normalize();
        if (current is not null &&
            !string.Equals(current.CourierId, normalized.CourierId, StringComparison.Ordinal))
            throw new ArgumentException(
                "The current position belongs to a different courier.",
                nameof(current));

        var observedAt = normalized.ObservedAt!.Value;
        if (current is not null && observedAt <= current.ObservedAt)
            return new CourierLocationProjectionResult(current, false);

        var position = new CourierCurrentPosition(
            normalized.CourierId,
            new GeoCoordinate(normalized.Latitude, normalized.Longitude),
            normalized.AccuracyMeters,
            normalized.HeadingDegrees,
            normalized.SpeedMetersPerSecond,
            observedAt,
            normalized.ActiveRouteId);

        return new CourierLocationProjectionResult(position, true);
    }
}
