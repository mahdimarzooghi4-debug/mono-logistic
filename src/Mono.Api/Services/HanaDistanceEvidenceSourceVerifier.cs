using Mono.Api.Domain;

namespace Mono.Api.Services;

public enum HanaDistanceEvidenceVerificationStatus
{
    Verified,
    Unverified,
    VerifierNotConfigured
}

public sealed record HanaDistanceEvidenceVerification(
    HanaDistanceEvidenceVerificationStatus Status,
    string ReasonCode);

/// <summary>
/// Verifies that measured distance evidence and its attestation came from a source
/// approved for the authenticated customer.
/// </summary>
public interface IHanaDistanceEvidenceSourceVerifier
{
    Task<HanaDistanceEvidenceVerification> VerifyAsync(
        string customerId,
        HanaDistancePricingEvidence evidence,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Safe default until Mono chooses a distance provider and a source-specific verifier.
/// </summary>
public sealed class UnconfiguredHanaDistanceEvidenceSourceVerifier : IHanaDistanceEvidenceSourceVerifier
{
    public Task<HanaDistanceEvidenceVerification> VerifyAsync(
        string customerId,
        HanaDistancePricingEvidence evidence,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new HanaDistanceEvidenceVerification(
            HanaDistanceEvidenceVerificationStatus.VerifierNotConfigured,
            "DISTANCE_SOURCE_VERIFIER_NOT_CONFIGURED"));
}
