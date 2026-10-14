using System.Globalization;
using Microsoft.Extensions.Configuration;
using Mono.Api.Domain;
using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class ConfiguredRouteTravelTimeEvidenceVerifierTests
{
    private const string CustomerId = "mono-route-evidence";
    private const string SourceId = "maps-routing-test";
    private const string KeyId = "route-key-1";
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(value => (byte)(value + 1)).ToArray();

    [Fact]
    public async Task Verifies_matrix_signature_bound_to_route_snapshot_candidate_and_directed_cells()
    {
        var request = CreateSignedRequest(Now.AddSeconds(-15));
        var verifier = CreateVerifier();

        var result = await verifier.VerifyAsync(request);

        Assert.Equal(RouteTravelTimeEvidenceVerificationStatus.Verified, result.Status);
        Assert.Equal("SOURCE_ATTESTATION_VERIFIED", result.ReasonCode);
    }

    [Fact]
    public async Task Rejects_a_matrix_cell_changed_after_source_attestation()
    {
        var signed = CreateSignedRequest(Now.AddSeconds(-15));
        var matrix = signed.Evidence.Matrix;
        var changedRow = new Dictionary<string, int?>(matrix.TravelTimeSeconds["A"], StringComparer.Ordinal)
        {
            ["B"] = 999
        };
        var changedMatrix = new Dictionary<string, IReadOnlyDictionary<string, int?>>(matrix.TravelTimeSeconds, StringComparer.Ordinal)
        {
            ["A"] = changedRow
        };
        var changed = signed with
        {
            Evidence = signed.Evidence with
            {
                Matrix = matrix with { TravelTimeSeconds = changedMatrix }
            }
        };

        var result = await CreateVerifier().VerifyAsync(changed);

        Assert.Equal(RouteTravelTimeEvidenceVerificationStatus.Unverified, result.Status);
        Assert.Equal("SOURCE_ATTESTATION_INVALID", result.ReasonCode);
    }

    [Fact]
    public async Task Rejects_changed_travel_mode_or_location_coordinate_after_source_attestation()
    {
        var signed = CreateSignedRequest(Now.AddSeconds(-15));
        var verifier = CreateVerifier();

        var modeResult = await verifier.VerifyAsync(signed with
        {
            Evidence = signed.Evidence with { TravelMode = RouteTravelMode.Walk }
        });

        var changedCoordinates = signed.Evidence.LocationCoordinates!
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        changedCoordinates["A"] = new GeoCoordinate(35.99, 51.41);
        var locationResult = await verifier.VerifyAsync(signed with
        {
            Evidence = signed.Evidence with { LocationCoordinates = changedCoordinates }
        });

        Assert.Equal(RouteTravelTimeEvidenceVerificationStatus.Unverified, modeResult.Status);
        Assert.Equal(RouteTravelTimeEvidenceVerificationStatus.Unverified, locationResult.Status);
    }

    [Fact]
    public async Task Rejects_reuse_of_evidence_for_a_different_route_version()
    {
        var signed = CreateSignedRequest(Now.AddSeconds(-15));
        var changed = signed with { RouteVersion = signed.RouteVersion + 1 };

        var result = await CreateVerifier().VerifyAsync(changed);

        Assert.Equal(RouteTravelTimeEvidenceVerificationStatus.Unverified, result.Status);
        Assert.Equal("SOURCE_ATTESTATION_INVALID", result.ReasonCode);
    }

    [Fact]
    public async Task Rejects_incomplete_or_negative_matrix_before_signature_acceptance()
    {
        var signed = CreateSignedRequest(Now.AddSeconds(-15));
        var matrix = signed.Evidence.Matrix;
        var incompleteRow = new Dictionary<string, int?>(matrix.TravelTimeSeconds["A"], StringComparer.Ordinal);
        incompleteRow.Remove("B");
        var incomplete = signed with
        {
            Evidence = signed.Evidence with
            {
                Matrix = matrix with
                {
                    TravelTimeSeconds = new Dictionary<string, IReadOnlyDictionary<string, int?>>(matrix.TravelTimeSeconds)
                    {
                        ["A"] = incompleteRow
                    }
                }
            }
        };

        var result = await CreateVerifier().VerifyAsync(incomplete);

        Assert.Equal(RouteTravelTimeEvidenceVerificationStatus.Unverified, result.Status);
        Assert.Equal("ROUTE_TRAVEL_TIME_EVIDENCE_INVALID", result.ReasonCode);
    }

    [Fact]
    public async Task Rejects_negative_travel_duration_before_signature_acceptance()
    {
        var signed = CreateSignedRequest(Now.AddSeconds(-15));
        var matrix = signed.Evidence.Matrix;
        var negativeRow = new Dictionary<string, int?>(matrix.TravelTimeSeconds["A"], StringComparer.Ordinal)
        {
            ["B"] = -1
        };
        var changed = signed with
        {
            Evidence = signed.Evidence with
            {
                Matrix = matrix with
                {
                    TravelTimeSeconds = new Dictionary<string, IReadOnlyDictionary<string, int?>>(matrix.TravelTimeSeconds)
                    {
                        ["A"] = negativeRow
                    }
                }
            }
        };

        var result = await CreateVerifier().VerifyAsync(changed);

        Assert.Equal(RouteTravelTimeEvidenceVerificationStatus.Unverified, result.Status);
        Assert.Equal("ROUTE_TRAVEL_TIME_EVIDENCE_INVALID", result.ReasonCode);
    }

    [Fact]
    public async Task Requires_route_evidence_namespace_and_approved_shared_source_scope()
    {
        var request = CreateSignedRequest(Now.AddSeconds(-15));

        var missingNamespace = await CreateVerifier(configureNamespace: false).VerifyAsync(request);
        var wrongScope = await CreateVerifier(scope: "feasibility:Distance").VerifyAsync(request);

        Assert.Equal(RouteTravelTimeEvidenceVerificationStatus.VerifierNotConfigured, missingNamespace.Status);
        Assert.Equal("ROUTE_TRAVEL_TIME_TRUST_NAMESPACE_NOT_CONFIGURED", missingNamespace.ReasonCode);
        Assert.Equal(RouteTravelTimeEvidenceVerificationStatus.Unverified, wrongScope.Status);
        Assert.Equal("SOURCE_SCOPE_NOT_ALLOWED", wrongScope.ReasonCode);
    }

    [Fact]
    public async Task Rejects_stale_route_matrix_evidence_using_existing_source_freshness_policy()
    {
        var request = CreateSignedRequest(Now.AddHours(-2));

        var result = await CreateVerifier(maxAgeSeconds: 300).VerifyAsync(request);

        Assert.Equal(RouteTravelTimeEvidenceVerificationStatus.Unverified, result.Status);
        Assert.Equal("SOURCE_EVIDENCE_STALE", result.ReasonCode);
    }

    private static RouteTravelTimeEvidenceVerificationRequest CreateSignedRequest(DateTimeOffset capturedAt)
    {
        var ids = new[] { "start", "A", "B", "P2", "D2" };
        var matrix = ids.ToDictionary(
            origin => origin,
            origin => (IReadOnlyDictionary<string, int?>)ids
                .Where(destination => !StringComparer.Ordinal.Equals(origin, destination))
                .ToDictionary(destination => destination, _ => (int?)45, StringComparer.Ordinal),
            StringComparer.Ordinal);
        var evidence = new RouteTravelTimeEvidence(
            SourceId, "matrix-evidence-1", capturedAt, "", new RouteTravelTimeMatrix(true, false, matrix),
            RouteTravelMode.Bike,
            new Dictionary<string, GeoCoordinate>(StringComparer.Ordinal)
            {
                ["start"] = new(35.70, 51.40), ["A"] = new(35.71, 51.41),
                ["B"] = new(35.72, 51.42), ["P2"] = new(35.73, 51.43), ["D2"] = new(35.74, 51.44)
            });
        var unsigned = new RouteTravelTimeEvidenceVerificationRequest(
            "route-1",
            3,
            new RoutePlanSnapshot(
                "route-1", "start", Now.AddMinutes(-3),
                [new RouteMissionDeadline("mission-1", Now.AddHours(1))],
                [
                    new RouteInsertionStop("mission-1", RouteStopKind.Pickup, "A", 20, Now.AddMinutes(-3)),
                    new RouteInsertionStop("mission-1", RouteStopKind.Dropoff, "B", 30)
                ]),
            new RouteInsertionMission(
                "mission-2", "P2", "D2", Now, Now.AddHours(2), 15, 30),
            evidence);
        var sourcePayloadSha256 = RouteTravelTimeEvidenceContract.ComputeSourcePayloadSha256(unsigned);
        var attestation = EvidenceSourceAttestationContract.Create(
            CustomerId,
            SourceId,
            RouteTravelTimeEvidenceContract.Scope,
            evidence.EvidenceId,
            capturedAt,
            sourcePayloadSha256,
            KeyId,
            Key);
        return unsigned with { Evidence = evidence with { Attestation = attestation } };
    }

    private static ConfiguredRouteTravelTimeEvidenceVerifier CreateVerifier(
        bool configureNamespace = true,
        string scope = "feasibility:RouteInsertion",
        int maxAgeSeconds = 3_600)
    {
        var values = new Dictionary<string, string?>
        {
            ["Mono:EvidenceSources:Enabled"] = "true",
            ["Mono:EvidenceSources:TrustedSources:0:CustomerId"] = CustomerId,
            ["Mono:EvidenceSources:TrustedSources:0:SourceId"] = SourceId,
            ["Mono:EvidenceSources:TrustedSources:0:KeyId"] = KeyId,
            ["Mono:EvidenceSources:TrustedSources:0:KeyBase64"] = Convert.ToBase64String(Key),
            ["Mono:EvidenceSources:TrustedSources:0:Enabled"] = "true",
            ["Mono:EvidenceSources:TrustedSources:0:MaxEvidenceAgeSeconds"] = maxAgeSeconds.ToString(CultureInfo.InvariantCulture),
            ["Mono:EvidenceSources:TrustedSources:0:AllowedFutureClockSkewSeconds"] = "30",
            ["Mono:EvidenceSources:TrustedSources:0:Scopes:0"] = scope
        };
        if (configureNamespace)
            values["Mono:RoutePlanning:EvidenceCustomerId"] = CustomerId;

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var sourceVerifier = new ConfiguredEvidenceSourceVerifier(configuration, new FixedTimeProvider(Now));
        return new ConfiguredRouteTravelTimeEvidenceVerifier(configuration, sourceVerifier);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
