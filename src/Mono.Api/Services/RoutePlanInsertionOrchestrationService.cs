using Mono.Api.Domain;
using Mono.Api.Persistence;

namespace Mono.Api.Services;

public sealed record RouteTravelTimeEvidence(
    string SourceId,
    string EvidenceId,
    DateTimeOffset CapturedAt,
    string Attestation,
    RouteTravelTimeMatrix Matrix);

public sealed record RouteTravelTimeEvidenceVerificationRequest(
    string RouteId,
    int RouteVersion,
    RoutePlanSnapshot ExistingPlan,
    RouteInsertionMission Candidate,
    RouteTravelTimeEvidence Evidence);

public enum RouteTravelTimeEvidenceVerificationStatus
{
    Verified,
    Unverified,
    VerifierNotConfigured
}

public sealed record RouteTravelTimeEvidenceVerification(
    RouteTravelTimeEvidenceVerificationStatus Status,
    string ReasonCode);

/// <summary>
/// Verifies provider-specific provenance and that the attestation covers the exact route,
/// existing plan, candidate, and travel matrix supplied for planning.
/// </summary>
public interface IRouteTravelTimeEvidenceVerifier
{
    Task<RouteTravelTimeEvidenceVerification> VerifyAsync(
        RouteTravelTimeEvidenceVerificationRequest request,
        CancellationToken cancellationToken = default);
}

public enum RoutePlanInsertionOutcome
{
    Persisted,
    Replayed,
    RouteNotFound,
    Conflict,
    Unresolved,
    NoFeasiblePosition,
    PositionSelectionRequired,
    InvalidPositionSelection
}

public sealed record RoutePlanInsertionCommand(
    string CommandId,
    string RouteId,
    int ExpectedVersion,
    RouteInsertionMission Candidate,
    RouteTravelTimeEvidence Evidence,
    RouteInsertionCandidatePosition? SelectedPosition = null);

public sealed record RoutePlanInsertionResult(
    RoutePlanInsertionOutcome Outcome,
    RouteInsertionTimeWindowResult Evaluation,
    RoutePlanVersionWriteResult? WriteResult,
    string? ReasonCode = null);

/// <summary>
/// Re-evaluates a candidate against the current immutable route snapshot, requires verified
/// travel-time evidence, and persists only an explicitly selected feasible position.
/// This service does not dispatch or authorize a courier.
/// </summary>
public sealed class RoutePlanInsertionOrchestrationService(
    IRoutePlanVersionStore routePlans,
    IRouteTravelTimeEvidenceVerifier evidenceVerifier)
{
    public async Task<RoutePlanInsertionResult> EvaluateAndPersistAsync(
        RoutePlanInsertionCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCommand(command);

        var current = await routePlans.FindCurrentAsync(command.RouteId, cancellationToken);
        if (current is null)
            return Result(RoutePlanInsertionOutcome.RouteNotFound, EmptyEvaluation("ROUTE_NOT_FOUND"));
        if (current.Version < command.ExpectedVersion)
            return Result(RoutePlanInsertionOutcome.Conflict, EmptyEvaluation("ROUTE_VERSION_CONFLICT"));

        // Re-evaluate stale retries against their original snapshot so the repository can
        // return an idempotent replay for the same command, or a conflict for a changed write.
        var planningVersion = current.Version == command.ExpectedVersion
            ? current
            : await routePlans.FindVersionAsync(command.RouteId, command.ExpectedVersion, cancellationToken);
        if (planningVersion is null)
            return Result(RoutePlanInsertionOutcome.Conflict, EmptyEvaluation("ROUTE_VERSION_CONFLICT"));

        var snapshot = planningVersion.Snapshot;
        var verification = await evidenceVerifier.VerifyAsync(
            new RouteTravelTimeEvidenceVerificationRequest(
                command.RouteId, planningVersion.Version, snapshot, command.Candidate, command.Evidence),
            cancellationToken);
        var request = new RouteInsertionTimeWindowRequest(
            command.RouteId,
            planningVersion.Version,
            snapshot.StartLocationId,
            snapshot.StartedAt,
            snapshot.OrderedStops,
            snapshot.Missions,
            command.Candidate,
            command.Evidence.Matrix with
            {
                // The planner's legacy flag is set only after this verifier succeeds.
                SourceVerified = verification.Status == RouteTravelTimeEvidenceVerificationStatus.Verified
            });

        var evaluation = RouteInsertionTimeWindowPlanner.Evaluate(request);
        if (evaluation.Disposition == RouteInsertionTimeWindowDisposition.Unresolved)
        {
            var reason = verification.Status == RouteTravelTimeEvidenceVerificationStatus.Verified
                ? evaluation.ReasonCodes.FirstOrDefault()
                : verification.ReasonCode;
            return Result(RoutePlanInsertionOutcome.Unresolved, evaluation, reasonCode: reason);
        }
        if (evaluation.Disposition == RouteInsertionTimeWindowDisposition.NoCandidatePosition)
            return Result(RoutePlanInsertionOutcome.NoFeasiblePosition, evaluation);
        if (command.SelectedPosition is null)
            return Result(RoutePlanInsertionOutcome.PositionSelectionRequired, evaluation);

        var selected = command.SelectedPosition;
        if (!evaluation.CandidatePositions.Any(position =>
                position.PickupIndex == selected.PickupIndex &&
                position.DropoffIndex == selected.DropoffIndex))
        {
            return Result(
                RoutePlanInsertionOutcome.InvalidPositionSelection,
                evaluation,
                reasonCode: "SELECTED_POSITION_NOT_FEASIBLE");
        }

        var newSnapshot = Insert(snapshot, command.Candidate, selected);
        var write = await routePlans.PersistAsync(
            new RoutePlanVersionWriteCommand(command.CommandId, command.ExpectedVersion, newSnapshot),
            cancellationToken);
        return write.Outcome switch
        {
            RoutePlanVersionWriteOutcome.Persisted => Result(RoutePlanInsertionOutcome.Persisted, evaluation, write),
            RoutePlanVersionWriteOutcome.Replayed => Result(RoutePlanInsertionOutcome.Replayed, evaluation, write),
            RoutePlanVersionWriteOutcome.RouteNotFound => Result(RoutePlanInsertionOutcome.RouteNotFound, evaluation, write),
            _ => Result(RoutePlanInsertionOutcome.Conflict, evaluation, write)
        };
    }

    private static RoutePlanSnapshot Insert(
        RoutePlanSnapshot current,
        RouteInsertionMission candidate,
        RouteInsertionCandidatePosition selected)
    {
        var stops = current.OrderedStops.ToList();
        stops.Insert(selected.PickupIndex, new RouteInsertionStop(
            candidate.MissionId,
            RouteStopKind.Pickup,
            candidate.PickupLocationId,
            candidate.PickupServiceSeconds,
            candidate.PickupReadyAt));
        stops.Insert(selected.DropoffIndex, new RouteInsertionStop(
            candidate.MissionId,
            RouteStopKind.Dropoff,
            candidate.DropoffLocationId,
            candidate.DropoffServiceSeconds));
        return current with
        {
            Missions = current.Missions.Append(
                new RouteMissionDeadline(candidate.MissionId, candidate.CommittedDeliveryAt)).ToArray(),
            OrderedStops = stops
        };
    }

    private static void ValidateCommand(RoutePlanInsertionCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.CommandId) || command.CommandId != command.CommandId.Trim() ||
            command.CommandId.Length > 128 || string.IsNullOrWhiteSpace(command.RouteId) ||
            command.RouteId != command.RouteId.Trim() || command.RouteId.Length > 128 ||
            command.ExpectedVersion <= 0 || command.Candidate is null || command.Evidence is null ||
            string.IsNullOrWhiteSpace(command.Evidence.SourceId) ||
            string.IsNullOrWhiteSpace(command.Evidence.Attestation) || command.Evidence.Matrix is null)
        {
            throw new ArgumentException("Route plan insertion command is malformed.", nameof(command));
        }
    }

    private static RoutePlanInsertionResult Result(
        RoutePlanInsertionOutcome outcome,
        RouteInsertionTimeWindowResult evaluation,
        RoutePlanVersionWriteResult? writeResult = null,
        string? reasonCode = null) =>
        new(outcome, evaluation, writeResult, reasonCode);

    private static RouteInsertionTimeWindowResult EmptyEvaluation(string reasonCode) =>
        new(RouteInsertionTimeWindowDisposition.Unresolved, [], [reasonCode]);
}
