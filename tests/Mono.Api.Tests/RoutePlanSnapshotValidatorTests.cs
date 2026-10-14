using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class RoutePlanSnapshotValidatorTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Accepts_valid_single_mission_route_plan()
    {
        RoutePlanSnapshotValidator.Validate(new RoutePlanVersionWriteCommand(
            "command-1",
            0,
            Snapshot()));
    }

    [Fact]
    public void Rejects_duplicate_mission_deadlines_and_broken_stop_order()
    {
        var duplicate = Snapshot() with
        {
            Missions =
            [
                new RouteMissionDeadline("mission-1", Start.AddMinutes(20)),
                new RouteMissionDeadline("mission-1", Start.AddMinutes(25))
            ]
        };
        var reversed = Snapshot() with
        {
            OrderedStops =
            [
                new RouteInsertionStop("mission-1", RouteStopKind.Dropoff, "B", 0),
                new RouteInsertionStop("mission-1", RouteStopKind.Pickup, "A", 0, Start)
            ]
        };

        Assert.Throws<ArgumentException>(() => RoutePlanSnapshotValidator.Validate(
            new RoutePlanVersionWriteCommand("command-1", 0, duplicate)));
        Assert.Throws<ArgumentException>(() => RoutePlanSnapshotValidator.Validate(
            new RoutePlanVersionWriteCommand("command-1", 0, reversed)));
    }

    [Fact]
    public void Rejects_timestamp_precision_that_postgres_cannot_round_trip_exactly()
    {
        var snapshot = Snapshot() with { StartedAt = Start.AddTicks(1) };

        Assert.Throws<ArgumentException>(() => RoutePlanSnapshotValidator.Validate(
            new RoutePlanVersionWriteCommand("command-1", 0, snapshot)));
    }

    private static RoutePlanSnapshot Snapshot() => new(
        "route-1",
        "start",
        Start,
        [new RouteMissionDeadline("mission-1", Start.AddMinutes(20))],
        [
            new RouteInsertionStop("mission-1", RouteStopKind.Pickup, "A", 0, Start),
            new RouteInsertionStop("mission-1", RouteStopKind.Dropoff, "B", 0)
        ]);
}
