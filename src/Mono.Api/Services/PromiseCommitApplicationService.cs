using Mono.Api.Domain;
using Mono.Api.Persistence;

namespace Mono.Api.Services;

public enum PromiseCommitApplicationStatus
{
    Committed,
    Replayed,
    Conflict,
    DeliveryRequestNotFound,
    Rejected,
    Unresolved,
    ProposalMismatch,
    EvidenceIntegrityInvalid
}

public sealed record PromiseCommitApplicationResult(
    PromiseCommitApplicationStatus Status,
    FeasibilityOutcomeEvaluation Evaluation,
    PromiseCommitWriteResult? Persistence);

public interface IPromiseCommitStore
{
    Task<PromiseCommitWriteResult> CommitAsync(
        PromiseCommitProposal proposal,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Connects the E7.2 outcome evaluator to Promise persistence.
/// Only a resolved ACCEPTED or ALTERNATIVE_PROMISE matching the proposal may be stored.
/// </summary>
public sealed class PromiseCommitApplicationService(IPromiseCommitStore store)
{
    public async Task<PromiseCommitApplicationResult> EvaluateAndCommitAsync(
        IReadOnlyCollection<FeasibilityCriterionResult>? requestedCriteria,
        DateTimeOffset? requestedDeliveryAt,
        AlternativePromiseSearchEvidence? alternativeSearch,
        PromiseCommitProposal proposal,
        CancellationToken cancellationToken = default)
    {
        var evaluation = FeasibilityOutcomeEvaluator.Evaluate(
            requestedCriteria,
            requestedDeliveryAt,
            alternativeSearch);

        if (evaluation.Status == FeasibilityOutcomeEvaluationStatus.Unresolved)
        {
            return new PromiseCommitApplicationResult(
                PromiseCommitApplicationStatus.Unresolved,
                evaluation,
                null);
        }

        if (evaluation.Outcome == FeasibilityOutcome.Rejected)
        {
            return new PromiseCommitApplicationResult(
                PromiseCommitApplicationStatus.Rejected,
                evaluation,
                null);
        }

        if (evaluation.Outcome is null ||
            evaluation.ProposedDeliveryAt is null ||
            proposal.Outcome != evaluation.Outcome ||
            proposal.CommittedDeliveryAt != evaluation.ProposedDeliveryAt)
        {
            return new PromiseCommitApplicationResult(
                PromiseCommitApplicationStatus.ProposalMismatch,
                evaluation,
                null);
        }

        var evidenceSets = evaluation.Outcome == FeasibilityOutcome.Accepted
            ? new[] { requestedCriteria }
            : new[] { requestedCriteria, alternativeSearch?.Criteria };
        if (evidenceSets.Any(criteria => !FeasibilityEvidenceIntegrityContract.AreAllValid(criteria)))
        {
            return new PromiseCommitApplicationResult(
                PromiseCommitApplicationStatus.EvidenceIntegrityInvalid,
                evaluation,
                null);
        }

        PromiseCommitProposalValidator.Validate(proposal);
        var persistence = await store.CommitAsync(proposal, cancellationToken);
        var status = persistence.Outcome switch
        {
            PromiseCommitWriteOutcome.Committed => PromiseCommitApplicationStatus.Committed,
            PromiseCommitWriteOutcome.Replayed => PromiseCommitApplicationStatus.Replayed,
            PromiseCommitWriteOutcome.Conflict => PromiseCommitApplicationStatus.Conflict,
            PromiseCommitWriteOutcome.DeliveryRequestNotFound => PromiseCommitApplicationStatus.DeliveryRequestNotFound,
            _ => throw new ArgumentOutOfRangeException(nameof(persistence), "Persistence outcome is not supported.")
        };

        return new PromiseCommitApplicationResult(status, evaluation, persistence);
    }
}
