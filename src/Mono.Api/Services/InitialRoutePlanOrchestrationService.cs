using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Mono.Api.Domain;
using Mono.Api.Persistence;

namespace Mono.Api.Services;

public sealed record InitialRoutePlanEvidenceVerificationRequest(
    string RouteId,
    string StartLocationId,
    DateTimeOffset StartedAt,
    string PromiseId,
    int PromiseVersion,
    RouteInsertionMission Mission,
    RouteTravelTimeEvidence Evidence);

public interface IInitialRoutePlanEvidenceVerifier
{
    Task<RouteTravelTimeEvidenceVerification> VerifyAsync(
        InitialRoutePlanEvidenceVerificationRequest request,
        CancellationToken cancellationToken = default);
}

public static class InitialRoutePlanEvidenceContract
{
    public static bool IsValid(InitialRoutePlanEvidenceVerificationRequest? request)
    {
        if (request is null || request.Evidence is null || request.Mission is null ||
            string.IsNullOrWhiteSpace(request.RouteId) || string.IsNullOrWhiteSpace(request.StartLocationId) ||
            string.IsNullOrWhiteSpace(request.PromiseId) || request.PromiseVersion <= 0 ||
            request.StartedAt == default || request.StartedAt.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(request.Evidence.SourceId) || string.IsNullOrWhiteSpace(request.Evidence.EvidenceId) ||
            string.IsNullOrWhiteSpace(request.Evidence.Attestation) || request.Evidence.CapturedAt == default ||
            request.Evidence.CapturedAt.Offset != TimeSpan.Zero || request.Evidence.TravelMode is null ||
            !Enum.IsDefined(request.Evidence.TravelMode.Value) || request.Evidence.LocationCoordinates is null ||
            request.Evidence.Matrix is not { IsComplete: true, TravelTimeSeconds: not null })
            return false;

        var expectedIds = new[] { request.StartLocationId, request.Mission.PickupLocationId,
                request.Mission.DropoffLocationId }.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (request.Evidence.LocationCoordinates.Count != expectedIds.Length ||
            expectedIds.Any(id => !request.Evidence.LocationCoordinates.TryGetValue(id, out var coordinate) ||
                                  coordinate is null || !coordinate.IsValid))
            return false;

        var matrix = request.Evidence.Matrix.TravelTimeSeconds;
        if (matrix.Count != expectedIds.Length)
            return false;
        foreach (var origin in expectedIds)
        {
            if (!matrix.TryGetValue(origin, out var row) || row is null || row.Count != expectedIds.Length)
                return false;
            foreach (var destination in expectedIds)
                if (!row.TryGetValue(destination, out var seconds) || seconds is < 0)
                    return false;
        }

        try
        {
            var result = InitialRoutePlanEvaluator.Evaluate(new InitialRoutePlanRequest(
                request.RouteId, request.StartLocationId, request.StartedAt, request.Mission,
                request.Evidence.Matrix with { SourceVerified = true }));
            return result.Disposition != InitialRoutePlanDisposition.Unresolved;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string ComputeSourcePayloadSha256(InitialRoutePlanEvidenceVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValid(request))
            throw new ArgumentException("Initial route evidence is malformed or incomplete.", nameof(request));

        var evidence = request.Evidence;
        var ids = evidence.LocationCoordinates!.Keys.Order(StringComparer.Ordinal).ToArray();
        var canonical = JsonSerializer.Serialize(new
        {
            contract = "mono-initial-route-plan-evidence.v1",
            route_id = request.RouteId,
            route_version = 0,
            start_location_id = request.StartLocationId,
            started_at = Utc(request.StartedAt),
            promise_id = request.PromiseId,
            promise_version = request.PromiseVersion,
            candidate = new
            {
                mission_id = request.Mission.MissionId,
                pickup_location_id = request.Mission.PickupLocationId,
                dropoff_location_id = request.Mission.DropoffLocationId,
                pickup_ready_at = Utc(request.Mission.PickupReadyAt),
                committed_delivery_at = Utc(request.Mission.CommittedDeliveryAt),
                pickup_service_seconds = request.Mission.PickupServiceSeconds,
                dropoff_service_seconds = request.Mission.DropoffServiceSeconds
            },
            source_id = evidence.SourceId,
            evidence_id = evidence.EvidenceId,
            captured_at = Utc(evidence.CapturedAt),
            travel_mode = evidence.TravelMode!.Value.ToString(),
            locations = ids.Select(id => new
            {
                location = id,
                latitude = evidence.LocationCoordinates[id].Latitude.ToString("R", CultureInfo.InvariantCulture),
                longitude = evidence.LocationCoordinates[id].Longitude.ToString("R", CultureInfo.InvariantCulture)
            }).ToArray(),
            directed_travel_time_seconds = ids.Select(origin => new
            {
                origin,
                destinations = ids.Select(destination => new
                {
                    location = destination,
                    seconds = evidence.Matrix.TravelTimeSeconds[origin][destination]
                }).ToArray()
            }).ToArray()
        });
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

/// <summary>Verifies initialization evidence through the configured trusted-source registry.</summary>
public sealed class ConfiguredInitialRoutePlanEvidenceVerifier(
    IConfiguration configuration,
    IFeasibilityEvidenceSourceVerifier sourceVerifier) : IInitialRoutePlanEvidenceVerifier
{
    public async Task<RouteTravelTimeEvidenceVerification> VerifyAsync(
        InitialRoutePlanEvidenceVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!InitialRoutePlanEvidenceContract.IsValid(request))
            return new RouteTravelTimeEvidenceVerification(
                RouteTravelTimeEvidenceVerificationStatus.Unverified, "INITIAL_ROUTE_EVIDENCE_INVALID");

        var customerId = configuration["Mono:RoutePlanning:EvidenceCustomerId"];
        if (string.IsNullOrWhiteSpace(customerId))
            return new RouteTravelTimeEvidenceVerification(
                RouteTravelTimeEvidenceVerificationStatus.VerifierNotConfigured,
                "ROUTE_TRAVEL_TIME_TRUST_NAMESPACE_NOT_CONFIGURED");

        var evidence = request.Evidence;
        var digest = InitialRoutePlanEvidenceContract.ComputeSourcePayloadSha256(request);
        var integrity = new FeasibilityEvidenceIntegrity(
            evidence.EvidenceId, evidence.CapturedAt, digest, evidence.SourceId, evidence.Attestation, digest);
        var verification = await sourceVerifier.VerifyAsync(
            customerId, FeasibilityCriterion.RouteInsertion, integrity, cancellationToken);
        return verification.Status switch
        {
            FeasibilityEvidenceSourceVerificationStatus.Verified => new(
                RouteTravelTimeEvidenceVerificationStatus.Verified, verification.ReasonCode),
            FeasibilityEvidenceSourceVerificationStatus.Unverified => new(
                RouteTravelTimeEvidenceVerificationStatus.Unverified, verification.ReasonCode),
            _ => new(RouteTravelTimeEvidenceVerificationStatus.VerifierNotConfigured, verification.ReasonCode)
        };
    }
}

public enum InitialRoutePlanWriteOutcome
{
    Persisted,
    Replayed,
    MissionUnavailable,
    Unresolved,
    Infeasible,
    Conflict
}

public sealed record InitialRoutePlanWriteCommand(
    string CommandId,
    string RouteId,
    string StartLocationId,
    DateTimeOffset StartedAt,
    string MissionId,
    int PickupServiceSeconds,
    int DropoffServiceSeconds,
    RouteTravelTimeEvidence Evidence);

public sealed record InitialRouteMissionCandidate(
    string MissionId,
    string DeliveryRequestId,
    string PromiseId,
    int PromiseVersion,
    RouteInsertionMission Candidate);

public interface IInitialRouteMissionCandidateSource
{
    Task<InitialRouteMissionCandidate?> FindConfirmedAsync(
        string missionId,
        int pickupServiceSeconds,
        int dropoffServiceSeconds,
        CancellationToken cancellationToken = default);
}

public sealed record InitialRoutePlanWriteResult(
    InitialRoutePlanWriteOutcome Outcome,
    InitialRoutePlanEvaluation Evaluation,
    RoutePlanVersionWriteResult? WriteResult = null,
    string? ReasonCode = null);

/// <summary>Creates version 1 only after trusted travel evidence proves the first Mission fits its Promise.</summary>
public sealed class InitialRoutePlanOrchestrationService(
    IRoutePlanVersionStore routePlans,
    IInitialRouteMissionCandidateSource missions,
    IInitialRoutePlanEvidenceVerifier evidenceVerifier)
{
    public async Task<InitialRoutePlanWriteResult> InitializeAsync(
        InitialRoutePlanWriteCommand command,
        CancellationToken cancellationToken = default)
    {
        Validate(command);
        var mission = await missions.FindConfirmedAsync(
            command.MissionId, command.PickupServiceSeconds, command.DropoffServiceSeconds, cancellationToken);
        var missionUnavailable = new InitialRoutePlanEvaluation(
            InitialRoutePlanDisposition.Unresolved, null, null, ["CONFIRMED_MISSION_OR_PROMISE_UNAVAILABLE"]);
        if (mission is null)
            return new(InitialRoutePlanWriteOutcome.MissionUnavailable, missionUnavailable,
                ReasonCode: "CONFIRMED_MISSION_OR_PROMISE_UNAVAILABLE");

        var unresolved = new InitialRoutePlanEvaluation(
            InitialRoutePlanDisposition.Unresolved, null, null, ["INITIAL_ROUTE_EVIDENCE_UNVERIFIED"]);

        var verification = await evidenceVerifier.VerifyAsync(new InitialRoutePlanEvidenceVerificationRequest(
            command.RouteId, command.StartLocationId, command.StartedAt, mission.PromiseId, mission.PromiseVersion,
            mission.Candidate, command.Evidence), cancellationToken);
        if (verification.Status != RouteTravelTimeEvidenceVerificationStatus.Verified)
            return new(InitialRoutePlanWriteOutcome.Unresolved, unresolved, ReasonCode: verification.ReasonCode);

        var evaluation = InitialRoutePlanEvaluator.Evaluate(new InitialRoutePlanRequest(
            command.RouteId, command.StartLocationId, command.StartedAt, mission.Candidate,
            command.Evidence.Matrix with { SourceVerified = true }));
        if (evaluation.Disposition == InitialRoutePlanDisposition.Unresolved)
            return new(InitialRoutePlanWriteOutcome.Unresolved, evaluation, ReasonCode: evaluation.ReasonCodes.FirstOrDefault());
        if (evaluation.Disposition == InitialRoutePlanDisposition.Infeasible)
            return new(InitialRoutePlanWriteOutcome.Infeasible, evaluation, ReasonCode: evaluation.ReasonCodes.FirstOrDefault());

        var write = await routePlans.PersistAsync(new RoutePlanVersionWriteCommand(
            command.CommandId, 0, evaluation.Snapshot!), cancellationToken);
        return write.Outcome switch
        {
            RoutePlanVersionWriteOutcome.Persisted => new(InitialRoutePlanWriteOutcome.Persisted, evaluation, write),
            RoutePlanVersionWriteOutcome.Replayed => new(InitialRoutePlanWriteOutcome.Replayed, evaluation, write),
            RoutePlanVersionWriteOutcome.RouteNotFound => new(InitialRoutePlanWriteOutcome.Conflict, evaluation, write, "ROUTE_NOT_FOUND"),
            _ => new(InitialRoutePlanWriteOutcome.Conflict, evaluation, write, "ROUTE_VERSION_CONFLICT")
        };
    }

    private static void Validate(InitialRoutePlanWriteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.CommandId) || command.CommandId != command.CommandId.Trim() ||
            command.CommandId.Length > 128 || command.Evidence is null || string.IsNullOrWhiteSpace(command.MissionId) ||
            command.PickupServiceSeconds is < 0 or > RouteInsertionTimeWindowPlanner.MaximumPickupDwellSeconds ||
            command.DropoffServiceSeconds < 0 ||
            command.Evidence.Matrix is null || string.IsNullOrWhiteSpace(command.Evidence.SourceId) ||
            string.IsNullOrWhiteSpace(command.Evidence.Attestation))
            throw new ArgumentException("Initial route write command is malformed.", nameof(command));
    }
}
