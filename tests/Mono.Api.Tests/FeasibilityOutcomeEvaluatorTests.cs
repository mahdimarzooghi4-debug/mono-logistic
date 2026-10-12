using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class FeasibilityOutcomeEvaluatorTests
{
    [Fact]
    public void RequestedPromiseIsAcceptedOnlyWhenEveryCriterionPasses()
    {
        var requestedAt = DateTimeOffset.UnixEpoch.AddHours(1);

        var result = FeasibilityOutcomeEvaluator.Evaluate(
            PassingCriteria(),
            requestedAt,
            alternativeSearch: null);

        Assert.Equal(FeasibilityOutcomeEvaluationStatus.Resolved, result.Status);
        Assert.Equal(FeasibilityOutcome.Accepted, result.Outcome);
        Assert.Equal(requestedAt, result.ProposedDeliveryAt);
        Assert.Empty(result.ReasonCodes);
    }

    [Fact]
    public void FeasibleAlternativeIsReturnedOnlyWhenItsFullGatePasses()
    {
        var requestedAt = DateTimeOffset.UnixEpoch.AddHours(1);
        var alternativeAt = requestedAt.AddMinutes(30);

        var result = FeasibilityOutcomeEvaluator.Evaluate(
            FailingRequestedCriteria(),
            requestedAt,
            Search(
                AlternativePromiseSearchStatus.CompleteFeasibleAlternative,
                alternativeAt,
                PassingCriteria()));

        Assert.Equal(FeasibilityOutcomeEvaluationStatus.Resolved, result.Status);
        Assert.Equal(FeasibilityOutcome.AlternativePromise, result.Outcome);
        Assert.Equal(alternativeAt, result.ProposedDeliveryAt);
    }

    [Fact]
    public void RequestIsRejectedOnlyAfterCompleteSearchFindsNoFeasibleAlternative()
    {
        var result = FeasibilityOutcomeEvaluator.Evaluate(
            FailingRequestedCriteria(),
            DateTimeOffset.UnixEpoch.AddHours(1),
            Search(
                AlternativePromiseSearchStatus.CompleteNoFeasibleAlternative,
                null,
                null,
                "ALTERNATIVE_SEARCH_EXHAUSTED"));

        Assert.Equal(FeasibilityOutcomeEvaluationStatus.Resolved, result.Status);
        Assert.Equal(FeasibilityOutcome.Rejected, result.Outcome);
        Assert.Null(result.ProposedDeliveryAt);
        Assert.Contains("NO_FEASIBLE_ALTERNATIVE_PROMISE", result.ReasonCodes);
    }

    [Fact]
    public void UnresolvedRequestedGateDoesNotProduceFinalOutcome()
    {
        var partial = PassingCriteria().Where(x => x.Criterion != FeasibilityCriterion.Coverage).ToArray();
        var result = FeasibilityOutcomeEvaluator.Evaluate(
            partial,
            DateTimeOffset.UnixEpoch.AddHours(1),
            Search(
                AlternativePromiseSearchStatus.CompleteNoFeasibleAlternative,
                null,
                null,
                "ALTERNATIVE_SEARCH_EXHAUSTED"));

        Assert.Equal(FeasibilityOutcomeEvaluationStatus.Unresolved, result.Status);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public void MissingRequestedEvidenceOrTimeRemainsUnresolved()
    {
        var noEvidence = FeasibilityOutcomeEvaluator.Evaluate(
            null,
            DateTimeOffset.UnixEpoch,
            alternativeSearch: null);
        var noTime = FeasibilityOutcomeEvaluator.Evaluate(
            PassingCriteria(),
            null,
            alternativeSearch: null);

        Assert.Equal(FeasibilityOutcomeEvaluationStatus.Unresolved, noEvidence.Status);
        Assert.Equal(FeasibilityOutcomeEvaluationStatus.Unresolved, noTime.Status);
        Assert.Null(noEvidence.Outcome);
        Assert.Null(noTime.Outcome);
    }

    [Fact]
    public void MissingOrIncompleteAlternativeSearchCannotReject()
    {
        var noSearch = FeasibilityOutcomeEvaluator.Evaluate(
            FailingRequestedCriteria(),
            DateTimeOffset.UnixEpoch,
            alternativeSearch: null);
        var incomplete = FeasibilityOutcomeEvaluator.Evaluate(
            FailingRequestedCriteria(),
            DateTimeOffset.UnixEpoch,
            Search(
                AlternativePromiseSearchStatus.Incomplete,
                null,
                null,
                "ALTERNATIVE_SEARCH_INCOMPLETE"));

        Assert.Equal(FeasibilityOutcomeEvaluationStatus.Unresolved, noSearch.Status);
        Assert.Equal(FeasibilityOutcomeEvaluationStatus.Unresolved, incomplete.Status);
        Assert.Null(noSearch.Outcome);
        Assert.Null(incomplete.Outcome);
    }

    [Fact]
    public void UnavailableAlternativeSearchCannotReject()
    {
        var result = FeasibilityOutcomeEvaluator.Evaluate(
            FailingRequestedCriteria(),
            DateTimeOffset.UnixEpoch,
            Search(
                AlternativePromiseSearchStatus.Unavailable,
                null,
                null,
                "ALTERNATIVE_SEARCH_UNAVAILABLE"));

        Assert.Equal(FeasibilityOutcomeEvaluationStatus.Unresolved, result.Status);
        Assert.Null(result.Outcome);
    }

    [Fact]
    public void ClaimedAlternativeMustHavePassingCriteriaAndDifferentTime()
    {
        var requestedAt = DateTimeOffset.UnixEpoch.AddHours(1);
        var sameTime = Search(
            AlternativePromiseSearchStatus.CompleteFeasibleAlternative,
            requestedAt,
            PassingCriteria());
        var failedGate = Search(
            AlternativePromiseSearchStatus.CompleteFeasibleAlternative,
            requestedAt.AddMinutes(30),
            FailingRequestedCriteria());

        Assert.Throws<ArgumentException>(() => FeasibilityOutcomeEvaluator.Evaluate(
            FailingRequestedCriteria(), requestedAt, sameTime));
        Assert.Throws<ArgumentException>(() => FeasibilityOutcomeEvaluator.Evaluate(
            FailingRequestedCriteria(), requestedAt, failedGate));
    }

    [Fact]
    public void NegativeSearchRequiresReasonAndNoCandidateData()
    {
        var missingReason = Search(
            AlternativePromiseSearchStatus.CompleteNoFeasibleAlternative,
            null,
            null);
        var contradictory = Search(
            AlternativePromiseSearchStatus.CompleteNoFeasibleAlternative,
            DateTimeOffset.UnixEpoch.AddHours(2),
            null,
            "ALTERNATIVE_SEARCH_EXHAUSTED");

        Assert.Throws<ArgumentException>(() => FeasibilityOutcomeEvaluator.Evaluate(
            FailingRequestedCriteria(), DateTimeOffset.UnixEpoch, missingReason));
        Assert.Throws<ArgumentException>(() => FeasibilityOutcomeEvaluator.Evaluate(
            FailingRequestedCriteria(), DateTimeOffset.UnixEpoch, contradictory));
    }

    [Fact]
    public void InvalidSearchIdsTimesStatusesAndReasonCodesAreRejected()
    {
        var invalidId = Search(
            AlternativePromiseSearchStatus.Incomplete,
            null,
            null,
            "SEARCH_INCOMPLETE") with { SearchId = " " };
        var invalidTime = Search(
            AlternativePromiseSearchStatus.Incomplete,
            null,
            null,
            "SEARCH_INCOMPLETE") with { SearchedAt = default };
        var invalidReason = Search(
            AlternativePromiseSearchStatus.Incomplete,
            null,
            null,
            "bad_reason");

        Assert.Throws<ArgumentException>(() => FeasibilityOutcomeEvaluator.Evaluate(
            FailingRequestedCriteria(), DateTimeOffset.UnixEpoch, invalidId));
        Assert.Throws<ArgumentException>(() => FeasibilityOutcomeEvaluator.Evaluate(
            FailingRequestedCriteria(), DateTimeOffset.UnixEpoch, invalidTime));
        Assert.Throws<ArgumentException>(() => FeasibilityOutcomeEvaluator.Evaluate(
            FailingRequestedCriteria(), DateTimeOffset.UnixEpoch, invalidReason));
    }

    private static IReadOnlyCollection<FeasibilityCriterionResult> PassingCriteria() =>
        FeasibilityEvidenceGate.Required
            .Select(criterion => new FeasibilityCriterionResult(
                criterion,
                FeasibilityCriterionStatus.Pass,
                []))
            .ToArray();

    private static IReadOnlyCollection<FeasibilityCriterionResult> FailingRequestedCriteria() =>
        FeasibilityEvidenceGate.Required
            .Select(criterion => new FeasibilityCriterionResult(
                criterion,
                criterion == FeasibilityCriterion.SlaFeasibility
                    ? FeasibilityCriterionStatus.Fail
                    : FeasibilityCriterionStatus.Pass,
                criterion == FeasibilityCriterion.SlaFeasibility
                    ? ["SLA_DEADLINE_MISSED"]
                    : []))
            .ToArray();

    private static AlternativePromiseSearchEvidence Search(
        AlternativePromiseSearchStatus status,
        DateTimeOffset? proposedAt,
        IReadOnlyCollection<FeasibilityCriterionResult>? criteria,
        params string[] reasonCodes) =>
        new("search-1", DateTimeOffset.UnixEpoch, status, proposedAt, criteria, reasonCodes);
}
