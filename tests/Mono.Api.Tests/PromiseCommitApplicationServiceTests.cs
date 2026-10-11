using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class PromiseCommitApplicationServiceTests
{
    [Fact]
    public async Task Persists_accepted_outcome_with_exact_requested_time()
    {
        var store = new RecordingStore();
        var service = new PromiseCommitApplicationService(store);
        var requestedAt = DateTimeOffset.UnixEpoch.AddHours(2);

        var result = await service.EvaluateAndCommitAsync(
            PassingCriteria(),
            requestedAt,
            null,
            Proposal(FeasibilityOutcome.Accepted, requestedAt));

        Assert.Equal(PromiseCommitApplicationStatus.Committed, result.Status);
        Assert.Equal(FeasibilityOutcome.Accepted, result.Evaluation.Outcome);
        Assert.Single(store.Proposals);
    }

    [Fact]
    public async Task Persists_alternative_only_when_it_matches_evaluated_time_and_outcome()
    {
        var store = new RecordingStore();
        var service = new PromiseCommitApplicationService(store);
        var requestedAt = DateTimeOffset.UnixEpoch.AddHours(2);
        var alternativeAt = requestedAt.AddMinutes(30);

        var result = await service.EvaluateAndCommitAsync(
            FailingCriteria(),
            requestedAt,
            Search(AlternativePromiseSearchStatus.CompleteFeasibleAlternative, alternativeAt, PassingCriteria()),
            Proposal(FeasibilityOutcome.AlternativePromise, alternativeAt));

        Assert.Equal(PromiseCommitApplicationStatus.Committed, result.Status);
        Assert.Equal(alternativeAt, store.Proposals.Single().CommittedDeliveryAt);
    }

    [Fact]
    public async Task Rejected_and_unresolved_evaluations_never_reach_persistence()
    {
        var store = new RecordingStore();
        var service = new PromiseCommitApplicationService(store);
        var requestedAt = DateTimeOffset.UnixEpoch.AddHours(2);

        var rejected = await service.EvaluateAndCommitAsync(
            FailingCriteria(),
            requestedAt,
            Search(
                AlternativePromiseSearchStatus.CompleteNoFeasibleAlternative,
                null,
                null,
                "ALTERNATIVE_SEARCH_EXHAUSTED"),
            Proposal(FeasibilityOutcome.Accepted, requestedAt));
        var unresolved = await service.EvaluateAndCommitAsync(
            FailingCriteria(),
            requestedAt,
            null,
            Proposal(FeasibilityOutcome.Accepted, requestedAt));

        Assert.Equal(PromiseCommitApplicationStatus.Rejected, rejected.Status);
        Assert.Equal(PromiseCommitApplicationStatus.Unresolved, unresolved.Status);
        Assert.Empty(store.Proposals);
    }

    [Fact]
    public async Task Mismatched_outcome_or_delivery_time_never_reaches_persistence()
    {
        var store = new RecordingStore();
        var service = new PromiseCommitApplicationService(store);
        var requestedAt = DateTimeOffset.UnixEpoch.AddHours(2);

        var wrongOutcome = await service.EvaluateAndCommitAsync(
            PassingCriteria(),
            requestedAt,
            null,
            Proposal(FeasibilityOutcome.AlternativePromise, requestedAt));
        var wrongTime = await service.EvaluateAndCommitAsync(
            PassingCriteria(),
            requestedAt,
            null,
            Proposal(FeasibilityOutcome.Accepted, requestedAt.AddMinutes(1)));

        Assert.Equal(PromiseCommitApplicationStatus.ProposalMismatch, wrongOutcome.Status);
        Assert.Equal(PromiseCommitApplicationStatus.ProposalMismatch, wrongTime.Status);
        Assert.Empty(store.Proposals);
    }

    [Fact]
    public async Task Missing_criterion_evidence_never_reaches_persistence()
    {
        var store = new RecordingStore();
        var service = new PromiseCommitApplicationService(store);
        var partial = PassingCriteria().Where(x => x.Criterion != FeasibilityCriterion.Coverage).ToArray();

        var result = await service.EvaluateAndCommitAsync(
            partial,
            DateTimeOffset.UnixEpoch.AddHours(2),
            null,
            Proposal(FeasibilityOutcome.Accepted, DateTimeOffset.UnixEpoch.AddHours(2)));

        Assert.Equal(PromiseCommitApplicationStatus.Unresolved, result.Status);
        Assert.Empty(store.Proposals);
    }

    private static IReadOnlyCollection<FeasibilityCriterionResult> PassingCriteria() =>
        FeasibilityEvidenceGate.Required
            .Select(criterion => new FeasibilityCriterionResult(
                criterion,
                FeasibilityCriterionStatus.Pass,
                []))
            .ToArray();

    private static IReadOnlyCollection<FeasibilityCriterionResult> FailingCriteria() =>
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
        new("search-1", DateTimeOffset.UnixEpoch.AddHours(1), status, proposedAt, criteria, reasonCodes);

    private static PromiseCommitProposal Proposal(FeasibilityOutcome outcome, DateTimeOffset deliveryAt) =>
        new(
            "prm_11111111111111111111111111111111",
            "drq_22222222222222222222222222222222",
            "decision-1",
            1,
            DateTimeOffset.UnixEpoch.AddHours(1),
            deliveryAt,
            outcome,
            TimeSpan.FromMinutes(5),
            "buffer-v1",
            new Dictionary<string, string> { ["promise"] = "v1" },
            ["PROMISE_FEASIBLE"]);

    private sealed class RecordingStore : IPromiseCommitStore
    {
        public List<PromiseCommitProposal> Proposals { get; } = [];

        public Task<PromiseCommitWriteResult> CommitAsync(
            PromiseCommitProposal proposal,
            CancellationToken cancellationToken = default)
        {
            Proposals.Add(proposal);
            return Task.FromResult(new PromiseCommitWriteResult(
                PromiseCommitWriteOutcome.Committed,
                proposal.PromiseId,
                proposal.Version,
                "evt_33333333333333333333333333333333"));
        }
    }
}
