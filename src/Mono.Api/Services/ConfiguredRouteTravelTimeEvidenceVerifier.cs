using Microsoft.Extensions.Configuration;
using Mono.Api.Domain;

namespace Mono.Api.Services;

/// <summary>
/// Adapts the shared evidence-source registry to route insertion matrices. An explicit
/// Mono:RoutePlanning:EvidenceCustomerId setting is required to select the trust namespace.
/// </summary>
public sealed class ConfiguredRouteTravelTimeEvidenceVerifier(
    IConfiguration configuration,
    IFeasibilityEvidenceSourceVerifier sourceVerifier) : IRouteTravelTimeEvidenceVerifier
{
    public async Task<RouteTravelTimeEvidenceVerification> VerifyAsync(
        RouteTravelTimeEvidenceVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!RouteTravelTimeEvidenceContract.IsValid(request))
        {
            return new RouteTravelTimeEvidenceVerification(
                RouteTravelTimeEvidenceVerificationStatus.Unverified,
                "ROUTE_TRAVEL_TIME_EVIDENCE_INVALID");
        }

        var customerId = configuration["Mono:RoutePlanning:EvidenceCustomerId"];
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return new RouteTravelTimeEvidenceVerification(
                RouteTravelTimeEvidenceVerificationStatus.VerifierNotConfigured,
                "ROUTE_TRAVEL_TIME_TRUST_NAMESPACE_NOT_CONFIGURED");
        }

        var evidence = request.Evidence;
        var sourcePayloadSha256 = RouteTravelTimeEvidenceContract.ComputeSourcePayloadSha256(request);
        var integrity = new FeasibilityEvidenceIntegrity(
            evidence.EvidenceId,
            evidence.CapturedAt,
            sourcePayloadSha256,
            evidence.SourceId,
            evidence.Attestation,
            sourcePayloadSha256);
        var verification = await sourceVerifier.VerifyAsync(
            customerId,
            FeasibilityCriterion.RouteInsertion,
            integrity,
            cancellationToken);

        return verification.Status switch
        {
            FeasibilityEvidenceSourceVerificationStatus.Verified => new RouteTravelTimeEvidenceVerification(
                RouteTravelTimeEvidenceVerificationStatus.Verified, verification.ReasonCode),
            FeasibilityEvidenceSourceVerificationStatus.Unverified => new RouteTravelTimeEvidenceVerification(
                RouteTravelTimeEvidenceVerificationStatus.Unverified, verification.ReasonCode),
            _ => new RouteTravelTimeEvidenceVerification(
                RouteTravelTimeEvidenceVerificationStatus.VerifierNotConfigured, verification.ReasonCode)
        };
    }
}
