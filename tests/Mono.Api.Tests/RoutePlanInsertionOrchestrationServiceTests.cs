using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class RoutePlanInsertionOrchestrationServiceTests
{
    private static readonly DateTimeOffset StartedAt = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Persists_only_an_explicitly_selected_verified_feasible_position()
    {
        var store = new FakeRoutePlanStore(CurrentPlan());
        var verifier = new FakeEvidenceVerifier(RouteTravelTimeEvidenceVerificationStatus.Verified);
        var service = new RoutePlanInsertionOrchestrationService(store, verifier);
        var command = Command();

        var preview = await service.EvaluateAndPersistAsync(command);

        Assert.Equal(RoutePlanInsertionOutcome.PositionSelectionRequired, preview.Outcome);
        Assert.NotEmpty(preview.Evaluation.CandidatePositions);
        Assert.Equal(0, store.PersistCount);
        Assert.True(verifier.WasCalled);

        var selected = preview.Evaluation.CandidatePositions[0];
        var persisted = await service.EvaluateAndPersistAsync(command with { SelectedPosition = selected });

        Assert.Equal(RoutePlanInsertionOutcome.Persisted, persisted.Outcome);
        Assert.Equal(1, store.PersistCount);
        Assert.NotNull(store.Current);
        Assert.Equal(2, store.Current.Version);
        Assert.Equal(2, store.Current.Snapshot.Missions.Count);
        Assert.Equal("mission-1", store.Current.Snapshot.OrderedStops[0].MissionId);
        Assert.Contains(store.Current.Snapshot.OrderedStops, stop => stop.MissionId == "mission-2");
    }

    [Fact]
    public async Task Unverified_evidence_never_reaches_persistence_even_when_matrix_flag_is_true()
    {
        var store = new FakeRoutePlanStore(CurrentPlan());
        var verifier = new FakeEvidenceVerifier(RouteTravelTimeEvidenceVerificationStatus.Unverified);
        var service = new RoutePlanInsertionOrchestrationService(store, verifier);

        var result = await service.EvaluateAndPersistAsync(Command());

        Assert.Equal(RoutePlanInsertionOutcome.Unresolved, result.Outcome);
        Assert.Equal("TEST_EVIDENCE_UNVERIFIED", result.ReasonCode);
        Assert.Equal(0, store.PersistCount);
    }

    [Fact]
    public async Task Rejects_a_position_that_was_not_returned_as_feasible()
    {
        var store = new FakeRoutePlanStore(CurrentPlan());
        var service = new RoutePlanInsertionOrchestrationService(
            store,
            new FakeEvidenceVerifier(RouteTravelTimeEvidenceVerificationStatus.Verified));

        var result = await service.EvaluateAndPersistAsync(Command() with
        {
            SelectedPosition = new RouteInsertionCandidatePosition(99, 100, new Dictionary<string, DateTimeOffset>())
        });

        Assert.Equal(RoutePlanInsertionOutcome.InvalidPositionSelection, result.Outcome);
        Assert.Equal(0, store.PersistCount);
    }

    [Fact]
    public async Task Fails_on_a_stale_route_version_before_verifying_or_writing()
    {
        var store = new FakeRoutePlanStore(CurrentPlan());
        var verifier = new FakeEvidenceVerifier(RouteTravelTimeEvidenceVerificationStatus.Verified);
        var service = new RoutePlanInsertionOrchestrationService(store, verifier);

        var result = await service.EvaluateAndPersistAsync(Command() with { ExpectedVersion = 2 });

        Assert.Equal(RoutePlanInsertionOutcome.Conflict, result.Outcome);
        Assert.False(verifier.WasCalled);
        Assert.Equal(0, store.PersistCount);
    }

    private static RoutePlanInsertionCommand Command()
    {
        var ids = new[] { "start", "A", "B", "P2", "D2" };
        var matrix = ids.ToDictionary(
            origin => origin,
            origin => (IReadOnlyDictionary<string, int?>)ids
                .Where(destination => destination != origin)
                .ToDictionary(destination => destination, _ => (int?)60, StringComparer.Ordinal),
            StringComparer.Ordinal);
        return new RoutePlanInsertionCommand(
            "command-2",
            "route-1",
            1,
            new RouteInsertionMission(
                "mission-2", "P2", "D2", StartedAt.AddMinutes(5), StartedAt.AddHours(2), 15, 30),
            new RouteTravelTimeEvidence(
                "provider-test", "evidence-2", StartedAt, "signed-test-attestation",
                new RouteTravelTimeMatrix(true, true, matrix), RouteTravelMode.Driving,
                ids.ToDictionary(id => id, id => new GeoCoordinate(35.7, 51.4), StringComparer.Ordinal)));
    }

    private static RoutePlanVersion CurrentPlan() => new(
        1,
        new RoutePlanSnapshot(
            "route-1",
            "start",
            StartedAt,
            [new RouteMissionDeadline("mission-1", StartedAt.AddHours(1))],
            [
                new RouteInsertionStop("mission-1", RouteStopKind.Pickup, "A", 15, StartedAt),
                new RouteInsertionStop("mission-1", RouteStopKind.Dropoff, "B", 30)
            ]),
        StartedAt);

    private sealed class FakeEvidenceVerifier(RouteTravelTimeEvidenceVerificationStatus status)
        : IRouteTravelTimeEvidenceVerifier
    {
        public bool WasCalled { get; private set; }

        public Task<RouteTravelTimeEvidenceVerification> VerifyAsync(
            RouteTravelTimeEvidenceVerificationRequest request,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(new RouteTravelTimeEvidenceVerification(status, "TEST_EVIDENCE_UNVERIFIED"));
        }
    }

    private sealed class FakeRoutePlanStore(RoutePlanVersion current) : IRoutePlanVersionStore
    {
        public RoutePlanVersion? Current { get; private set; } = current;
        public int PersistCount { get; private set; }

        public Task<RoutePlanVersion?> FindCurrentAsync(
            string routeId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current?.Snapshot.RouteId == routeId ? Current : null);

        public Task<RoutePlanVersion?> FindVersionAsync(
            string routeId,
            int version,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Current is { } current &&
                            current.Snapshot.RouteId == routeId && current.Version == version
                ? current
                : null);

        public Task<RoutePlanVersionWriteResult> PersistAsync(
            RoutePlanVersionWriteCommand command,
            CancellationToken cancellationToken = default)
        {
            PersistCount++;
            RoutePlanSnapshotValidator.Validate(command);
            var version = command.ExpectedVersion + 1;
            Current = new RoutePlanVersion(version, command.Snapshot, StartedAt);
            return Task.FromResult(new RoutePlanVersionWriteResult(
                RoutePlanVersionWriteOutcome.Persisted, command.Snapshot.RouteId, version));
        }
    }
}
