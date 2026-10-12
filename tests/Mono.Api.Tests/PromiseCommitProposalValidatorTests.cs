using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class PromiseCommitProposalValidatorTests
{
    [Theory]
    [InlineData(FeasibilityOutcome.Accepted)]
    [InlineData(FeasibilityOutcome.AlternativePromise)]
    public void ValidPromiseProposalPasses(FeasibilityOutcome outcome) =>
        PromiseCommitProposalValidator.Validate(Proposal(outcome));

    [Fact]
    public void RejectedOutcomeCannotBeCommittedAsPromise() =>
        Assert.Throws<ArgumentException>(() =>
            PromiseCommitProposalValidator.Validate(Proposal(FeasibilityOutcome.Rejected)));

    [Fact]
    public void DeliveryMustFollowEvaluationAndVersionMustBePositive()
    {
        Assert.Throws<ArgumentException>(() =>
            PromiseCommitProposalValidator.Validate(Proposal() with { CommittedDeliveryAt = DateTimeOffset.UnixEpoch }));
        Assert.Throws<ArgumentException>(() =>
            PromiseCommitProposalValidator.Validate(Proposal() with { Version = 0 }));
    }

    [Fact]
    public void SafetyBufferMustBeNonNegativeAndHavePolicyVersion()
    {
        Assert.Throws<ArgumentException>(() =>
            PromiseCommitProposalValidator.Validate(Proposal() with { SafetyBuffer = TimeSpan.FromSeconds(-1) }));
        Assert.Throws<ArgumentException>(() =>
            PromiseCommitProposalValidator.Validate(Proposal() with { SafetyBufferPolicyVersion = " " }));
    }

    [Fact]
    public void PolicyVersionEvidenceIsRequired()
    {
        Assert.Throws<ArgumentException>(() =>
            PromiseCommitProposalValidator.Validate(Proposal() with {
                PolicyVersions = new Dictionary<string, string>()
            }));
        Assert.Throws<ArgumentException>(() =>
            PromiseCommitProposalValidator.Validate(Proposal() with {
                PolicyVersions = new Dictionary<string, string> { ["sla"] = "" }
            }));
    }

    [Fact]
    public void ReasonEvidenceMustBePresentCanonicalAndUnique()
    {
        Assert.Throws<ArgumentException>(() =>
            PromiseCommitProposalValidator.Validate(Proposal() with { ReasonCodes = [] }));
        Assert.Throws<ArgumentException>(() =>
            PromiseCommitProposalValidator.Validate(Proposal() with { ReasonCodes = ["bad_code"] }));
        Assert.Throws<ArgumentException>(() =>
            PromiseCommitProposalValidator.Validate(Proposal() with {
                ReasonCodes = ["PROMISE_FEASIBLE", "PROMISE_FEASIBLE"]
            }));
    }

    [Fact]
    public void IdentifiersMustMatchMonoFormats()
    {
        Assert.Throws<ArgumentException>(() =>
            PromiseCommitProposalValidator.Validate(Proposal() with { PromiseId = "promise-1" }));
        Assert.Throws<ArgumentException>(() =>
            PromiseCommitProposalValidator.Validate(Proposal() with { DeliveryRequestId = "request-1" }));
    }

    private static PromiseCommitProposal Proposal(
        FeasibilityOutcome outcome = FeasibilityOutcome.Accepted) =>
        new(
            "prm_11111111111111111111111111111111",
            "drq_22222222222222222222222222222222",
            "decision-1",
            1,
            DateTimeOffset.UnixEpoch.AddHours(1),
            DateTimeOffset.UnixEpoch.AddHours(2),
            outcome,
            TimeSpan.FromMinutes(5),
            "buffer-v1",
            new Dictionary<string, string> { ["promise"] = "v1" },
            ["PROMISE_FEASIBLE"]);
}
