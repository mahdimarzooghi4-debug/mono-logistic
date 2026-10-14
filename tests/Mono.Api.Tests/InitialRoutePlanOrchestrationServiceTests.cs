using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class InitialRoutePlanOrchestrationServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Evaluates_a_verified_first_mission_and_builds_initial_snapshot()
    {
        var result = InitialRoutePlanEvaluator.Evaluate(Request(Matrix(sourceVerified: true)));

        Assert.Equal(InitialRoutePlanDisposition.Feasible, result.Disposition);
        Assert.Equal(Start.AddMinutes(7).AddSeconds(45), result.EstimatedDeliveryAt);
        Assert.Equal("route-1", result.Snapshot!.RouteId);
        Assert.Equal(2, result.Snapshot.OrderedStops.Count);
        Assert.Equal("mission-1", result.Snapshot.Missions.Single().MissionId);
    }

    [Fact]
    public void Fails_closed_for_unverified_or_missing_directed_evidence()
    {
        var unverified = InitialRoutePlanEvaluator.Evaluate(Request(Matrix(sourceVerified: false)));
        Assert.Equal(InitialRoutePlanDisposition.Unresolved, unverified.Disposition);
        Assert.Contains("TRAVEL_MATRIX_SOURCE_UNVERIFIED", unverified.ReasonCodes);

        var missingCell = Matrix(sourceVerified: true).TravelTimeSeconds.ToDictionary(
            pair => pair.Key,
            pair => pair.Key == "pickup"
                ? (IReadOnlyDictionary<string, int?>)new Dictionary<string, int?> { ["start"] = 60, ["pickup"] = 0 }
                : pair.Value,
            StringComparer.Ordinal);
        var incomplete = InitialRoutePlanEvaluator.Evaluate(Request(
            new RouteTravelTimeMatrix(true, true, missingCell)));
        Assert.Equal(InitialRoutePlanDisposition.Unresolved, incomplete.Disposition);
        Assert.Contains("TRAVEL_MATRIX_INCOMPLETE", incomplete.ReasonCodes);
    }

    [Fact]
    public void Rejects_a_first_mission_that_misses_its_committed_promise()
    {
        var request = Request(Matrix(sourceVerified: true)) with
        {
            Mission = Mission() with { CommittedDeliveryAt = Start.AddMinutes(7) }
        };

        var result = InitialRoutePlanEvaluator.Evaluate(request);

        Assert.Equal(InitialRoutePlanDisposition.Infeasible, result.Disposition);
        Assert.Contains("MISSION_PROMISE_DEADLINE_MISSED", result.ReasonCodes);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public async Task Persists_only_when_source_verification_and_initial_simulation_pass()
    {
        var store = new FakeRoutePlanStore();
        var verifier = new FakeEvidenceVerifier(RouteTravelTimeEvidenceVerificationStatus.Verified);
        var service = new InitialRoutePlanOrchestrationService(store, new FakeMissionSource(), verifier);

        var result = await service.InitializeAsync(Command());

        Assert.Equal(InitialRoutePlanWriteOutcome.Persisted, result.Outcome);
        Assert.True(verifier.WasCalled);
        Assert.Equal(1, store.PersistCount);
        Assert.Equal(1, store.Current!.Version);
        Assert.Equal(2, store.Current.Snapshot.OrderedStops.Count);
    }

    [Fact]
    public async Task Does_not_persist_unverified_or_infeasible_initial_route()
    {
        var store = new FakeRoutePlanStore();
        var unverified = new InitialRoutePlanOrchestrationService(store,
            new FakeMissionSource(), new FakeEvidenceVerifier(RouteTravelTimeEvidenceVerificationStatus.Unverified));
        var result = await unverified.InitializeAsync(Command());
        Assert.Equal(InitialRoutePlanWriteOutcome.Unresolved, result.Outcome);
        Assert.Equal(0, store.PersistCount);

        var feasibleVerifier = new FakeEvidenceVerifier(RouteTravelTimeEvidenceVerificationStatus.Verified);
        var initializer = new InitialRoutePlanOrchestrationService(store,
            new FakeMissionSource(candidate: Mission() with { CommittedDeliveryAt = Start.AddMinutes(7) }),
            feasibleVerifier);
        var late = await initializer.InitializeAsync(Command());
        Assert.Equal(InitialRoutePlanWriteOutcome.Infeasible, late.Outcome);
        Assert.Equal(1, store.PersistCount);
    }

    [Fact]
    public void Evidence_contract_binds_complete_initial_route_context()
    {
        var request = new InitialRoutePlanEvidenceVerificationRequest(
            "route-1", "start", Start, "promise-1", 1, Mission(), Evidence());

        Assert.True(InitialRoutePlanEvidenceContract.IsValid(request));
        var digest = InitialRoutePlanEvidenceContract.ComputeSourcePayloadSha256(request);
        Assert.Equal(64, digest.Length);
        Assert.NotEqual(digest, InitialRoutePlanEvidenceContract.ComputeSourcePayloadSha256(
            request with { StartedAt = Start.AddMinutes(1) }));
    }

    [Fact]
    public async Task Does_not_initialize_when_confirmed_mission_or_promise_cannot_be_loaded()
    {
        var store = new FakeRoutePlanStore();
        var verifier = new FakeEvidenceVerifier(RouteTravelTimeEvidenceVerificationStatus.Verified);
        var service = new InitialRoutePlanOrchestrationService(store, new FakeMissionSource(available: false), verifier);

        var result = await service.InitializeAsync(Command());

        Assert.Equal(InitialRoutePlanWriteOutcome.MissionUnavailable, result.Outcome);
        Assert.False(verifier.WasCalled);
        Assert.Equal(0, store.PersistCount);
    }

    private static InitialRoutePlanWriteCommand Command() => new(
        "initial-command-1", "route-1", "start", Start, "mission-1", 15, 30, Evidence());

    private static InitialRoutePlanRequest Request(RouteTravelTimeMatrix matrix) =>
        new("route-1", "start", Start, Mission(), matrix);

    private static RouteInsertionMission Mission() => new(
        "mission-1", "pickup", "dropoff", Start.AddMinutes(2), Start.AddMinutes(20), 15, 30);

    private static RouteTravelTimeMatrix Matrix(bool sourceVerified)
    {
        var ids = new[] { "start", "pickup", "dropoff" };
        var matrix = ids.ToDictionary(origin => origin,
            origin => (IReadOnlyDictionary<string, int?>)ids.ToDictionary(destination => destination,
                destination => (int?)(origin == destination ? 0 : origin == "start" && destination == "pickup" ? 60 :
                    origin == "pickup" && destination == "dropoff" ? 300 : 600), StringComparer.Ordinal), StringComparer.Ordinal);
        return new RouteTravelTimeMatrix(true, sourceVerified, matrix);
    }

    private static RouteTravelTimeEvidence Evidence()
    {
        var ids = new[] { "start", "pickup", "dropoff" };
        return new RouteTravelTimeEvidence("provider-test", "evidence-1", Start, "trusted-attestation",
            Matrix(sourceVerified: false), RouteTravelMode.Car,
            ids.ToDictionary(id => id, _ => new GeoCoordinate(35.7, 51.4), StringComparer.Ordinal));
    }

    private sealed class FakeEvidenceVerifier(RouteTravelTimeEvidenceVerificationStatus status)
        : IInitialRoutePlanEvidenceVerifier
    {
        public bool WasCalled { get; private set; }

        public Task<RouteTravelTimeEvidenceVerification> VerifyAsync(
            InitialRoutePlanEvidenceVerificationRequest request,
            CancellationToken cancellationToken = default)
        {
            WasCalled = true;
            return Task.FromResult(new RouteTravelTimeEvidenceVerification(status, "TEST_INITIAL_EVIDENCE"));
        }
    }

    private sealed class FakeMissionSource(bool available = true, RouteInsertionMission? candidate = null)
        : IInitialRouteMissionCandidateSource
    {
        public Task<InitialRouteMissionCandidate?> FindConfirmedAsync(
            string missionId,
            int pickupServiceSeconds,
            int dropoffServiceSeconds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<InitialRouteMissionCandidate?>(available
                ? new InitialRouteMissionCandidate(missionId, "drq_0123456789abcdef0123456789abcdef",
                    "promise-1", 1, (candidate ?? Mission()) with
                    {
                        MissionId = missionId,
                        PickupServiceSeconds = pickupServiceSeconds,
                        DropoffServiceSeconds = dropoffServiceSeconds
                    })
                : null);
    }

    private sealed class FakeRoutePlanStore : IRoutePlanVersionStore
    {
        public RoutePlanVersion? Current { get; private set; }
        public int PersistCount { get; private set; }

        public Task<RoutePlanVersion?> FindCurrentAsync(string routeId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Current?.Snapshot.RouteId == routeId ? Current : null);

        public Task<RoutePlanVersion?> FindVersionAsync(string routeId, int version, CancellationToken cancellationToken = default) =>
            Task.FromResult(Current?.Snapshot.RouteId == routeId && Current.Version == version ? Current : null);

        public Task<RoutePlanVersionWriteResult> PersistAsync(
            RoutePlanVersionWriteCommand command, CancellationToken cancellationToken = default)
        {
            PersistCount++;
            RoutePlanSnapshotValidator.Validate(command);
            Current = new RoutePlanVersion(command.ExpectedVersion + 1, command.Snapshot, Start);
            return Task.FromResult(new RoutePlanVersionWriteResult(
                RoutePlanVersionWriteOutcome.Persisted, command.Snapshot.RouteId, command.ExpectedVersion + 1));
        }
    }
}
