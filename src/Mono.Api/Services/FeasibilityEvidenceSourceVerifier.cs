using Mono.Api.Domain;

namespace Mono.Api.Services;

public enum FeasibilityEvidenceSourceVerificationStatus
{
    Verified,
    Unverified,
    VerifierNotConfigured
}

public sealed record FeasibilityEvidenceSourceVerification(
    FeasibilityEvidenceSourceVerificationStatus Status,
    string ReasonCode);

/// <summary>
/// Verifies that evidence came from an approved source for the authenticated customer.
/// Implementations must validate source identity and the source-specific attestation.
/// </summary>
public interface IFeasibilityEvidenceSourceVerifier
{
    Task<FeasibilityEvidenceSourceVerification> VerifyAsync(
        string customerId,
        FeasibilityCriterion criterion,
        FeasibilityEvidenceIntegrity integrity,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Safe default until Mono selects trusted evidence sources and their verification mechanism.
/// </summary>
public sealed class UnconfiguredFeasibilityEvidenceSourceVerifier : IFeasibilityEvidenceSourceVerifier
{
    public Task<FeasibilityEvidenceSourceVerification> VerifyAsync(
        string customerId,
        FeasibilityCriterion criterion,
        FeasibilityEvidenceIntegrity integrity,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new FeasibilityEvidenceSourceVerification(
            FeasibilityEvidenceSourceVerificationStatus.VerifierNotConfigured,
            "SOURCE_VERIFIER_NOT_CONFIGURED"));
}
