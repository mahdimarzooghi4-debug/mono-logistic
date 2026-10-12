using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class EligibleCourierFeasibilityCriterionTests
{
    [Fact]
    public void OneProvenEligibleCourierPassesEvenWhenCandidateSetIsIncomplete()
    {
        var result = EligibleCourierFeasibilityCriterion.Evaluate(Evidence(
            CourierCandidateSetStatus.Incomplete,
            [Candidate("courier-1", CourierEligibilityStatus.Eligible)],
            ["CANDIDATE_SCAN_INCOMPLETE"]));

        Assert.Equal(FeasibilityCriterion.EligibleCouriers, result.Criterion);
        Assert.Equal(FeasibilityCriterionStatus.Pass, result.Status);
        Assert.Empty(result.ReasonCodes);
    }

    [Fact]
    public void CompleteSetWithOnlyExcludedCouriersFails()
    {
        var result = EligibleCourierFeasibilityCriterion.Evaluate(Evidence(
            CourierCandidateSetStatus.Complete,
            [
                Candidate("courier-1", CourierEligibilityStatus.Excluded, "ZONE_NOT_APPROVED"),
                Candidate("courier-2", CourierEligibilityStatus.Excluded, "COURIER_OFFLINE")
            ]));

        Assert.Equal(FeasibilityCriterionStatus.Fail, result.Status);
        Assert.Equal(
            new[] { "NO_ELIGIBLE_COURIER", "ZONE_NOT_APPROVED", "COURIER_OFFLINE" },
            result.ReasonCodes);
    }

    [Fact]
    public void CompleteEmptySetProvesNoEligibleCourier()
    {
        var result = EligibleCourierFeasibilityCriterion.Evaluate(Evidence(
            CourierCandidateSetStatus.Complete,
            []));

        Assert.Equal(FeasibilityCriterionStatus.Fail, result.Status);
        Assert.Equal(new[] { "NO_ELIGIBLE_COURIER" }, result.ReasonCodes);
    }

    [Fact]
    public void IncompleteSetWithoutEligibleCourierRemainsUnresolved()
    {
        var result = EligibleCourierFeasibilityCriterion.Evaluate(Evidence(
            CourierCandidateSetStatus.Incomplete,
            [Candidate("courier-1", CourierEligibilityStatus.Excluded, "ZONE_NOT_APPROVED")],
            ["CANDIDATE_SCAN_INCOMPLETE"]));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Contains("CANDIDATE_SCAN_INCOMPLETE", result.ReasonCodes);
        Assert.DoesNotContain("NO_ELIGIBLE_COURIER", result.ReasonCodes);
    }

    [Fact]
    public void UnresolvedCandidatePreventsDefinitiveFailure()
    {
        var result = EligibleCourierFeasibilityCriterion.Evaluate(Evidence(
            CourierCandidateSetStatus.Complete,
            [
                Candidate("courier-1", CourierEligibilityStatus.Excluded, "COURIER_OFFLINE"),
                Candidate("courier-2", CourierEligibilityStatus.Unresolved, "LOCATION_STALE")
            ]));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Contains("LOCATION_STALE", result.ReasonCodes);
    }

    [Fact]
    public void UnavailableCandidateSourceRemainsUnresolved()
    {
        var result = EligibleCourierFeasibilityCriterion.Evaluate(Evidence(
            CourierCandidateSetStatus.Unavailable,
            [],
            ["COURIER_SOURCE_UNAVAILABLE"]));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "COURIER_SOURCE_UNAVAILABLE" }, result.ReasonCodes);
    }

    [Fact]
    public void IncompleteSetRequiresReasonCode()
    {
        Assert.Throws<ArgumentException>(() => EligibleCourierFeasibilityCriterion.Evaluate(
            Evidence(CourierCandidateSetStatus.Incomplete, [])));
    }

    [Fact]
    public void UnavailableSetCannotClaimCandidateResults()
    {
        Assert.Throws<ArgumentException>(() => EligibleCourierFeasibilityCriterion.Evaluate(
            Evidence(CourierCandidateSetStatus.Unavailable,
                [Candidate("courier-1", CourierEligibilityStatus.Eligible)],
                ["COURIER_SOURCE_UNAVAILABLE"])));
    }

    [Fact]
    public void DuplicateCandidateIdentifiersAreRejected()
    {
        Assert.Throws<ArgumentException>(() => EligibleCourierFeasibilityCriterion.Evaluate(
            Evidence(CourierCandidateSetStatus.Complete,
            [
                Candidate("courier-1", CourierEligibilityStatus.Excluded, "COURIER_OFFLINE"),
                Candidate("courier-1", CourierEligibilityStatus.Excluded, "COURIER_OFFLINE")
            ])));
    }

    [Fact]
    public void NonCanonicalReasonsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => EligibleCourierFeasibilityCriterion.Evaluate(
            Evidence(CourierCandidateSetStatus.Complete,
                [Candidate("courier-1", CourierEligibilityStatus.Excluded, "offline")])));
    }

    [Fact]
    public void UnknownCandidateStatusIsRejected()
    {
        Assert.Throws<ArgumentException>(() => EligibleCourierFeasibilityCriterion.Evaluate(
            Evidence(CourierCandidateSetStatus.Complete,
                [Candidate("courier-1", (CourierEligibilityStatus)99, "UNKNOWN_STATUS")])));
    }

    [Fact]
    public void PassingCriterionDoesNotCompleteTheAggregateGate()
    {
        var courier = EligibleCourierFeasibilityCriterion.Evaluate(Evidence(
            CourierCandidateSetStatus.Complete,
            [Candidate("courier-1", CourierEligibilityStatus.Eligible)]));
        var gate = FeasibilityEvidenceGate.Evaluate([courier]);

        Assert.Equal(FeasibilityGateStatus.Unresolved, gate.Status);
        Assert.Contains(FeasibilityCriterion.Coverage, gate.MissingCriteria);
    }

    private static CourierEligibilityEvidenceSet Evidence(
        CourierCandidateSetStatus setStatus,
        IReadOnlyCollection<CourierEligibilityCandidateResult> candidates,
        IReadOnlyList<string>? reasons = null) =>
        new(setStatus, candidates, reasons ?? []);

    private static CourierEligibilityCandidateResult Candidate(
        string candidateId,
        CourierEligibilityStatus status,
        params string[] reasons) =>
        new(candidateId, status, reasons);
}
