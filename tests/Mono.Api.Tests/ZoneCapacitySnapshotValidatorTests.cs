using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class ZoneCapacitySnapshotValidatorTests
{
    [Fact]
    public void Accepts_a_zone_snapshot_with_non_negative_counts()
    {
        var errors = ZoneCapacitySnapshotValidator.Validate(
            new ZoneCapacitySnapshot
            {
                ZoneId = " micro-1 ",
                ObservedAt = DateTimeOffset.Parse("2026-10-10T12:30:00+03:30"),
                RegisteredCount = 80,
                OnlineCount = 28,
                EffectiveCount = 11
            },
            ZoneRegistry());

        Assert.Empty(errors);
    }

    [Fact]
    public void Accepts_zero_capacity()
    {
        var errors = ZoneCapacitySnapshotValidator.Validate(
            new ZoneCapacitySnapshot
            {
                ZoneId = "micro-1",
                ObservedAt = DateTimeOffset.UtcNow,
                RegisteredCount = 0,
                OnlineCount = 0,
                EffectiveCount = 0
            },
            ZoneRegistry());

        Assert.Empty(errors);
    }

    [Fact]
    public void Rejects_missing_zone_and_observation_timestamp()
    {
        var errors = ZoneCapacitySnapshotValidator.Validate(
            new ZoneCapacitySnapshot(),
            ZoneRegistry());

        Assert.Contains(errors, error => error == "zone_id is required.");
        Assert.Contains(errors, error => error == "observed_at is required.");
    }

    [Fact]
    public void Zone_must_exist_in_registry()
    {
        var errors = ZoneCapacitySnapshotValidator.Validate(
            new ZoneCapacitySnapshot
            {
                ZoneId = "missing-zone",
                ObservedAt = DateTimeOffset.UtcNow
            },
            ZoneRegistry());

        Assert.Contains(errors, error => error == "zone_id must refer to a zone in the registry.");
    }

    [Theory]
    [InlineData(-1, 0, 0, "registered_count")]
    [InlineData(0, -1, 0, "online_count")]
    [InlineData(0, 0, -1, "effective_count")]
    public void Rejects_negative_counts(
        int registered,
        int online,
        int effective,
        string fieldName)
    {
        var errors = ZoneCapacitySnapshotValidator.Validate(
            new ZoneCapacitySnapshot
            {
                ZoneId = "micro-1",
                ObservedAt = DateTimeOffset.UtcNow,
                RegisteredCount = registered,
                OnlineCount = online,
                EffectiveCount = effective
            },
            ZoneRegistry());

        Assert.Contains(errors, error => error.StartsWith($"{fieldName} cannot be negative", StringComparison.Ordinal));
    }

    [Fact]
    public void Normalizes_zone_identifier()
    {
        var snapshot = new ZoneCapacitySnapshot { ZoneId = " micro-1 " };

        Assert.Equal("micro-1", snapshot.Normalize().ZoneId);
    }

    [Fact]
    public void Null_inputs_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ZoneCapacitySnapshotValidator.Validate(null!, ZoneRegistry()));
        Assert.Throws<ArgumentNullException>(() =>
            ZoneCapacitySnapshotValidator.Validate(new ZoneCapacitySnapshot(), null!));
    }

    private static ZoneDefinition[] ZoneRegistry() =>
    [
        new ZoneDefinition { ZoneId = "city-1", Kind = ZoneKind.City, Name = "Tehran" },
        new ZoneDefinition
        {
            ZoneId = "zone-1",
            Kind = ZoneKind.OperatingZone,
            Name = "Central",
            ParentZoneId = "city-1"
        },
        new ZoneDefinition
        {
            ZoneId = "micro-1",
            Kind = ZoneKind.MicroZone,
            Name = "Block A",
            ParentZoneId = "zone-1"
        }
    ];
}
