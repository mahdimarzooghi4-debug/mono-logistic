using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class RouteInsertionTimeWindowPlannerTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FindsInsertionThatPreservesExistingMissionOrderAndPromises()
    {
        var existing = new RouteMissionDeadline("existing", Start.AddMinutes(20));
        var stops = new[]
        {
            new RouteInsertionStop("existing", RouteStopKind.Pickup, "B", 0, Start),
            new RouteInsertionStop("existing", RouteStopKind.Dropoff, "C", 0)
        };
        var request = Request(
            stops,
            [existing],
            Candidate("new", "D", "E", Start, Start.AddMinutes(30)),
            Matrix("A", "B", "C", "D", "E"));

        var result = RouteInsertionTimeWindowPlanner.Evaluate(request);

        Assert.Equal(RouteInsertionTimeWindowDisposition.CandidatePositionsFound, result.Disposition);
        Assert.NotEmpty(result.CandidatePositions);
        Assert.All(result.CandidatePositions, position =>
        {
            Assert.True(position.PickupIndex < position.DropoffIndex);
            Assert.True(position.DeliveryTimes["existing"] <= existing.CommittedDeliveryAt);
            Assert.True(position.DeliveryTimes["new"] <= Start.AddMinutes(30));
        });
    }

    [Fact]
    public void ReturnsNoPositionWhenCandidatePromiseCannotBeMet()
    {
        var request = Request(
            [],
            [],
            Candidate("new", "B", "C", Start, Start.AddSeconds(30)),
            Matrix("A", "B", "C"));

        var result = RouteInsertionTimeWindowPlanner.Evaluate(request);

        Assert.Equal(RouteInsertionTimeWindowDisposition.NoCandidatePosition, result.Disposition);
        Assert.Contains("MISSION_PROMISE_DEADLINE_MISSED", result.ReasonCodes);
    }

    [Fact]
    public void SameLocationLegUsesZeroTravelTimeWithoutMatrixDiagonal()
    {
        var result = RouteInsertionTimeWindowPlanner.Evaluate(Request(
            [], [], Candidate("new", "A", "B", Start, Start.AddMinutes(2)), Matrix("A", "B")));

        Assert.Equal(RouteInsertionTimeWindowDisposition.CandidatePositionsFound, result.Disposition);
        Assert.All(result.CandidatePositions, position =>
            Assert.Equal(Start.AddMinutes(1), position.DeliveryTimes["new"]));
    }

    [Fact]
    public void PickupReadyWaitMustFitInsideThreeMinuteDwellLimit()
    {
        var atLimit = RouteInsertionTimeWindowPlanner.Evaluate(Request(
            [], [], Candidate("at-limit", "B", "C", Start.AddMinutes(4), Start.AddMinutes(10)), Matrix("A", "B", "C")));
        var overLimit = RouteInsertionTimeWindowPlanner.Evaluate(Request(
            [], [], Candidate("over-limit", "B", "C", Start.AddMinutes(4).AddSeconds(1), Start.AddMinutes(10)), Matrix("A", "B", "C")));

        Assert.Equal(RouteInsertionTimeWindowDisposition.CandidatePositionsFound, atLimit.Disposition);
        Assert.Equal(RouteInsertionTimeWindowDisposition.NoCandidatePosition, overLimit.Disposition);
        Assert.Contains("PICKUP_DWELL_LIMIT_EXCEEDED", overLimit.ReasonCodes);
    }

    [Fact]
    public void IncompleteOrUnverifiedTravelMatrixRemainsUnresolved()
    {
        var candidate = Candidate("new", "B", "C", Start, Start.AddMinutes(10));
        var incomplete = RouteInsertionTimeWindowPlanner.Evaluate(Request(
            [], [], candidate, new RouteTravelTimeMatrix(false, true, new Dictionary<string, IReadOnlyDictionary<string, int?>>())));
        var unverified = RouteInsertionTimeWindowPlanner.Evaluate(Request(
            [], [], candidate, new RouteTravelTimeMatrix(true, false, Matrix("A", "B", "C").TravelTimeSeconds)));

        Assert.Equal(RouteInsertionTimeWindowDisposition.Unresolved, incomplete.Disposition);
        Assert.Contains("TRAVEL_MATRIX_INCOMPLETE", incomplete.ReasonCodes);
        Assert.Equal(RouteInsertionTimeWindowDisposition.Unresolved, unverified.Disposition);
        Assert.Contains("TRAVEL_MATRIX_SOURCE_UNVERIFIED", unverified.ReasonCodes);
    }

    [Fact]
    public void RejectsSixthMissionBeforeEnumeratingPositions()
    {
        var missions = Enumerable.Range(1, 5)
            .Select(index => new RouteMissionDeadline($"m{index}", Start.AddHours(2)))
            .ToArray();
        var stops = missions.SelectMany((mission, index) => new[]
        {
            new RouteInsertionStop(mission.MissionId, RouteStopKind.Pickup, $"P{index}", 0, Start),
            new RouteInsertionStop(mission.MissionId, RouteStopKind.Dropoff, $"D{index}", 0)
        }).ToArray();
        var locations = new[] { "A", "B", "C" }.Concat(Enumerable.Range(0, 5).SelectMany(i => new[] { $"P{i}", $"D{i}" }));
        var result = RouteInsertionTimeWindowPlanner.Evaluate(Request(
            stops,
            missions,
            Candidate("m6", "B", "C", Start, Start.AddHours(2)),
            Matrix(locations.ToArray())));

        Assert.Equal(RouteInsertionTimeWindowDisposition.NoCandidatePosition, result.Disposition);
        Assert.Equal(new[] { "ROUTE_MISSION_LIMIT_REACHED" }, result.ReasonCodes);
    }

    [Fact]
    public void RejectsNullExistingStopAsMalformedInput()
    {
        var request = Request(
            [null!],
            [new RouteMissionDeadline("existing", Start.AddMinutes(10))],
            Candidate("new", "B", "C", Start, Start.AddMinutes(20)),
            Matrix("A", "B", "C"));

        Assert.Throws<ArgumentException>(() => RouteInsertionTimeWindowPlanner.Evaluate(request));
    }

    private static RouteInsertionTimeWindowRequest Request(
        IReadOnlyList<RouteInsertionStop> stops,
        IReadOnlyList<RouteMissionDeadline> missions,
        RouteInsertionMission candidate,
        RouteTravelTimeMatrix matrix) => new(
            "route-1", 1, "A", Start, stops, missions, candidate, matrix);

    private static RouteInsertionMission Candidate(
        string missionId,
        string pickup,
        string dropoff,
        DateTimeOffset readyAt,
        DateTimeOffset deadline) => new(missionId, pickup, dropoff, readyAt, deadline, 0, 0);

    private static RouteTravelTimeMatrix Matrix(params string[] locationIds)
    {
        var rows = locationIds.ToDictionary(
            origin => origin,
            origin => (IReadOnlyDictionary<string, int?>)locationIds
                .Where(destination => !StringComparer.Ordinal.Equals(origin, destination))
                .ToDictionary(destination => destination, _ => (int?)60, StringComparer.Ordinal),
            StringComparer.Ordinal);
        return new RouteTravelTimeMatrix(true, true, rows);
    }
}
