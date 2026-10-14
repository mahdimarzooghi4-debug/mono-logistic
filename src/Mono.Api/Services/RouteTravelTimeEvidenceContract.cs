using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mono.Api.Domain;

namespace Mono.Api.Services;

/// <summary>
/// Canonical source payload for a route-time matrix. The digest binds the evidence to the
/// exact immutable route version, insertion candidate, and directed matrix cells.
/// </summary>
public static class RouteTravelTimeEvidenceContract
{
    public const string Scope = "feasibility:RouteInsertion";

    public static bool IsValid(RouteTravelTimeEvidenceVerificationRequest? request)
        => IsValid(request, requireAttestation: true);

    private static bool IsValid(RouteTravelTimeEvidenceVerificationRequest? request, bool requireAttestation)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.RouteId) || request.RouteVersion <= 0 ||
            request.ExistingPlan is null || request.Candidate is null || request.Evidence is null ||
            string.IsNullOrWhiteSpace(request.Evidence.SourceId) ||
            string.IsNullOrWhiteSpace(request.Evidence.EvidenceId) ||
            (requireAttestation && string.IsNullOrWhiteSpace(request.Evidence.Attestation)) ||
            request.ExistingPlan.RouteId != request.RouteId ||
            !IsIdentifier(request.Candidate.MissionId) ||
            !IsIdentifier(request.Candidate.PickupLocationId) ||
            !IsIdentifier(request.Candidate.DropoffLocationId) ||
            request.Candidate.PickupReadyAt.Offset != TimeSpan.Zero ||
            request.Candidate.CommittedDeliveryAt.Offset != TimeSpan.Zero ||
            request.Candidate.CommittedDeliveryAt <= request.Candidate.PickupReadyAt ||
            request.Candidate.PickupServiceSeconds is < 0 or > RouteInsertionTimeWindowPlanner.MaximumPickupDwellSeconds ||
            request.Candidate.DropoffServiceSeconds < 0 ||
            request.Evidence.CapturedAt == default || request.Evidence.CapturedAt.Offset != TimeSpan.Zero ||
            request.Evidence.TravelMode is not { } travelMode || !Enum.IsDefined(travelMode) ||
            request.Evidence.LocationCoordinates is null ||
            request.Evidence.Matrix is not { IsComplete: true, TravelTimeSeconds: not null })
            return false;

        try
        {
            RoutePlanSnapshotValidator.Validate(new RoutePlanVersionWriteCommand(
                "route-evidence-validation", request.RouteVersion, request.ExistingPlan));
        }
        catch (ArgumentException)
        {
            return false;
        }

        var locationIds = request.ExistingPlan.OrderedStops
            .Select(stop => stop.LocationId)
            .Append(request.ExistingPlan.StartLocationId)
            .Append(request.Candidate.PickupLocationId)
            .Append(request.Candidate.DropoffLocationId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (request.Evidence.LocationCoordinates.Count != locationIds.Length ||
            locationIds.Any(id => !request.Evidence.LocationCoordinates.TryGetValue(id, out var coordinate) ||
                                  coordinate is null || !coordinate.IsValid))
            return false;
        var matrix = request.Evidence.Matrix.TravelTimeSeconds;
        if (matrix.Any(origin => origin.Value is null ||
                                 origin.Value.Any(destination => destination.Value is < 0)))
            return false;

        foreach (var origin in locationIds)
        {
            if (!matrix.TryGetValue(origin, out var row) || row is null)
                return false;
            foreach (var destination in locationIds)
            {
                if (StringComparer.Ordinal.Equals(origin, destination))
                    continue;
                if (!row.TryGetValue(destination, out var seconds) || seconds is < 0)
                    return false;
            }
        }

        return true;
    }

    public static string ComputeSourcePayloadSha256(RouteTravelTimeEvidenceVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IsValid(request, requireAttestation: false))
            throw new ArgumentException("Route travel-time evidence is malformed or incomplete.", nameof(request));

        var evidence = request.Evidence;
        var matrix = evidence.Matrix.TravelTimeSeconds;
        var canonical = JsonSerializer.Serialize(new
        {
            contract = "mono-route-travel-time-evidence.v2",
            route_id = request.RouteId,
            route_version = request.RouteVersion,
            route_snapshot_sha256 = RoutePlanSnapshotValidator.ComputeSha256(request.ExistingPlan),
            candidate = new
            {
                mission_id = request.Candidate.MissionId,
                pickup_location_id = request.Candidate.PickupLocationId,
                dropoff_location_id = request.Candidate.DropoffLocationId,
                pickup_ready_at = Utc(request.Candidate.PickupReadyAt),
                committed_delivery_at = Utc(request.Candidate.CommittedDeliveryAt),
                pickup_service_seconds = request.Candidate.PickupServiceSeconds,
                dropoff_service_seconds = request.Candidate.DropoffServiceSeconds
            },
            evidence_id = evidence.EvidenceId,
            captured_at = Utc(evidence.CapturedAt),
            source_id = evidence.SourceId,
            travel_mode = evidence.TravelMode!.Value.ToString(),
            locations = evidence.LocationCoordinates!
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new
                {
                    location = pair.Key,
                    latitude = pair.Value.Latitude.ToString("R", CultureInfo.InvariantCulture),
                    longitude = pair.Value.Longitude.ToString("R", CultureInfo.InvariantCulture)
                })
                .ToArray(),
            matrix_complete = evidence.Matrix.IsComplete,
            cells = matrix.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(origin => new
                {
                    origin = origin.Key,
                    destinations = origin.Value.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(destination => new { location = destination.Key, seconds = destination.Value })
                        .ToArray()
                })
                .ToArray()
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Utc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static bool IsIdentifier(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);
}
