using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class CourierLocationUpdateTests
{
    [Fact]
    public void Valid_update_normalizes_identifiers_and_timestamp_to_utc()
    {
        var input = ValidUpdate() with
        {
            CourierId = " crr_1 ",
            ActiveRouteId = " rte_1 ",
            ObservedAt = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.FromHours(3))
        };

        var normalized = input.Normalize();

        Assert.Empty(CourierLocationUpdateValidator.Validate(input));
        Assert.Equal("crr_1", normalized.CourierId);
        Assert.Equal("rte_1", normalized.ActiveRouteId);
        Assert.Equal(TimeSpan.Zero, normalized.ObservedAt!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 10, 8, 30, 0, TimeSpan.Zero), normalized.ObservedAt);
    }

    [Fact]
    public void Validator_rejects_missing_courier_and_observation_time()
    {
        var errors = CourierLocationUpdateValidator.Validate(ValidUpdate() with
        {
            CourierId = " ",
            ObservedAt = null
        });

        Assert.Contains(errors, error => error.StartsWith("courier_id", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("observed_at", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.PositiveInfinity)]
    public void Validator_rejects_invalid_coordinates(double latitude, double longitude)
    {
        var errors = CourierLocationUpdateValidator.Validate(ValidUpdate() with
        {
            Latitude = latitude,
            Longitude = longitude
        });

        Assert.Contains(errors, error => error.StartsWith("latitude and longitude", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_rejects_invalid_optional_measurements()
    {
        var errors = CourierLocationUpdateValidator.Validate(ValidUpdate() with
        {
            AccuracyMeters = -1,
            HeadingDegrees = 360,
            SpeedMetersPerSecond = double.NaN
        });

        Assert.Contains(errors, error => error.StartsWith("accuracy_meters", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("heading_degrees", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("speed_meters_per_second", StringComparison.Ordinal));
    }

    [Fact]
    public void Projector_applies_a_newer_position_and_preserves_route_context()
    {
        var result = CourierLocationProjector.Apply(null, ValidUpdate() with
        {
            ActiveRouteId = " rte_1 "
        });

        Assert.True(result.Applied);
        Assert.Equal("crr_1", result.Position.CourierId);
        Assert.Equal(new GeoCoordinate(35.7, 51.4), result.Position.Location);
        Assert.Equal("rte_1", result.Position.ActiveRouteId);
    }

    [Fact]
    public void Projector_ignores_older_and_equal_observation_times()
    {
        var current = CourierLocationProjector.Apply(null, ValidUpdate()).Position;

        var older = CourierLocationProjector.Apply(current, ValidUpdate() with
        {
            Latitude = 36,
            ObservedAt = current.ObservedAt.AddSeconds(-1)
        });
        var equal = CourierLocationProjector.Apply(current, ValidUpdate() with
        {
            Latitude = 37,
            ObservedAt = current.ObservedAt
        });

        Assert.False(older.Applied);
        Assert.False(equal.Applied);
        Assert.Equal(new GeoCoordinate(35.7, 51.4), older.Position.Location);
        Assert.Equal(new GeoCoordinate(35.7, 51.4), equal.Position.Location);
    }

    [Fact]
    public void Projector_rejects_events_for_a_different_courier()
    {
        var current = CourierLocationProjector.Apply(null, ValidUpdate()).Position;

        Assert.Throws<ArgumentException>(() =>
            CourierLocationProjector.Apply(current, ValidUpdate() with { CourierId = "crr_2" }));
    }

    [Fact]
    public void Freshness_uses_the_supplied_age_limit_and_rejects_future_observations()
    {
        var position = CourierLocationProjector.Apply(null, ValidUpdate()).Position;
        var now = position.ObservedAt.AddSeconds(30);

        Assert.True(position.IsFreshAt(now, TimeSpan.FromSeconds(30)));
        Assert.False(position.IsFreshAt(now, TimeSpan.FromSeconds(29)));
        Assert.False(position.IsFreshAt(position.ObservedAt.AddTicks(-1), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Freshness_requires_a_non_negative_supplied_age_limit()
    {
        var position = CourierLocationProjector.Apply(null, ValidUpdate()).Position;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            position.IsFreshAt(position.ObservedAt, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void Validator_accepts_absent_optional_sensor_values()
    {
        Assert.Empty(CourierLocationUpdateValidator.Validate(ValidUpdate() with
        {
            AccuracyMeters = null,
            HeadingDegrees = null,
            SpeedMetersPerSecond = null,
            ActiveRouteId = null
        }));
    }

    private static CourierLocationUpdate ValidUpdate() => new()
    {
        CourierId = "crr_1",
        Latitude = 35.7,
        Longitude = 51.4,
        AccuracyMeters = 12,
        HeadingDegrees = 180,
        SpeedMetersPerSecond = 3.5,
        ObservedAt = new DateTimeOffset(2026, 10, 10, 8, 30, 0, TimeSpan.Zero)
    };
}
