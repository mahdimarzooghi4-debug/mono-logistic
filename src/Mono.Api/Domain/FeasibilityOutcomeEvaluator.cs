using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public enum FeasibilityOutcome
{
    Accepted,
    AlternativePromise,
    Rejected
}

public enum FeasibilityOutcomeEvaluationStatus
{
    Resolved,
    Unresolved
}

public enum AlternativePromiseSearchStatus
{
    CompleteFeasibleAlternative,
    CompleteNoFeasibleAlternative,
    Incomplete,
    Unavailable
}

public sealed record AlternativePromiseSearchEvidence(
    string SearchId,
    DateTimeOffset SearchedAt,
    AlternativePromiseSearchStatus Status,
    DateTimeOffset? ProposedDeliveryAt,
    IReadOnlyCollection<FeasibilityCriterionResult>? Criteria,
    IReadOnlyList<string> ReasonCodes);

public sealed record FeasibilityOutcomeEvaluation(
    FeasibilityOutcomeEvaluationStatus Status,
    FeasibilityOutcome? Outcome,
    DateTimeOffset? ProposedDeliveryAt,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Resolves E7.2 outcomes from caller-supplied E7.1 evidence and alternative-search evidence.
/// It does not choose alternative times, run searches, or commit a Promise.
/// </summary>
public static class FeasibilityOutcomeEvaluator
{
    private static readonly Regex ReasonCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static FeasibilityOutcomeEvaluation Evaluate(
        IReadOnlyCollection<FeasibilityCriterionResult>? requestedCriteria,
        DateTimeOffset? requestedDeliveryAt,
        AlternativePromiseSearchEvidence? alternativeSearch)
    {
        if (requestedCriteria is null)
            return Unresolved("REQUESTED_FEASIBILITY_EVIDENCE_UNAVAILABLE");

        if (requestedDeliveryAt.HasValue && requestedDeliveryAt.Value == default)
            throw new ArgumentException("Requested delivery time is invalid.", nameof(requestedDeliveryAt));

        if (requestedDeliveryAt is null)
            return Unresolved("REQUESTED_DELIVERY_TIME_UNAVAILABLE");

        var requestedGate = FeasibilityEvidenceGate.Evaluate(requestedCriteria);
        if (requestedGate.Status == FeasibilityGateStatus.ReadyForOutcomeEvaluation)
        {
            return Resolved(FeasibilityOutcome.Accepted, requestedDeliveryAt);
        }

        if (requestedGate.Status == FeasibilityGateStatus.Unresolved)
            return Unresolved(requestedGate.ReasonCodes);

        if (alternativeSearch is null)
            return Unresolved("ALTERNATIVE_PROMISE_SEARCH_UNAVAILABLE");

        ValidateSearchEvidence(alternativeSearch);

        switch (alternativeSearch.Status)
        {
            case AlternativePromiseSearchStatus.Incomplete:
            case AlternativePromiseSearchStatus.Unavailable:
                return Unresolved(alternativeSearch.ReasonCodes);

            case AlternativePromiseSearchStatus.CompleteNoFeasibleAlternative:
                return Resolved(
                    FeasibilityOutcome.Rejected,
                    null,
                    new[] { "NO_FEASIBLE_ALTERNATIVE_PROMISE" }
                        .Concat(requestedGate.ReasonCodes)
                        .Concat(alternativeSearch.ReasonCodes)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray());

            case AlternativePromiseSearchStatus.CompleteFeasibleAlternative:
                if (alternativeSearch.ProposedDeliveryAt is null ||
                    alternativeSearch.Criteria is null ||
                    alternativeSearch.ReasonCodes.Count > 0)
                {
                    throw new ArgumentException(
                        "A feasible alternative requires a proposed time, complete criterion evidence, and no unresolved reasons.",
                        nameof(alternativeSearch));
                }

                if (alternativeSearch.ProposedDeliveryAt.Value == requestedDeliveryAt.Value)
                {
                    throw new ArgumentException(
                        "An alternative promise must differ from the requested delivery time.",
                        nameof(alternativeSearch));
                }

                var alternativeGate = FeasibilityEvidenceGate.Evaluate(alternativeSearch.Criteria);
                if (alternativeGate.Status != FeasibilityGateStatus.ReadyForOutcomeEvaluation)
                {
                    throw new ArgumentException(
                        "A feasible alternative must pass every E7.1 criterion.",
                        nameof(alternativeSearch));
                }

                return Resolved(
                    FeasibilityOutcome.AlternativePromise,
                    alternativeSearch.ProposedDeliveryAt,
                    ["ALTERNATIVE_PROMISE_AVAILABLE"]);

            default:
                throw new ArgumentException("Alternative search status is not supported.", nameof(alternativeSearch));
        }
    }

    private static void ValidateSearchEvidence(AlternativePromiseSearchEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.SearchId) ||
            evidence.SearchId.Length > 128 ||
            evidence.SearchId.Any(char.IsControl) ||
            evidence.SearchedAt == default ||
            !Enum.IsDefined(evidence.Status) ||
            evidence.ReasonCodes is null ||
            (evidence.ProposedDeliveryAt.HasValue && evidence.ProposedDeliveryAt.Value == default))
        {
            throw new ArgumentException("Alternative promise search evidence is malformed.", nameof(evidence));
        }

        ValidateReasonCodes(evidence.ReasonCodes, nameof(evidence));

        switch (evidence.Status)
        {
            case AlternativePromiseSearchStatus.Incomplete:
            case AlternativePromiseSearchStatus.Unavailable:
                if (evidence.ReasonCodes.Count == 0 ||
                    evidence.ProposedDeliveryAt is not null ||
                    evidence.Criteria is not null)
                {
                    throw new ArgumentException(
                        "Incomplete or unavailable searches require reasons and cannot claim a proposed alternative.",
                        nameof(evidence));
                }
                break;

            case AlternativePromiseSearchStatus.CompleteNoFeasibleAlternative:
                if (evidence.ReasonCodes.Count == 0 ||
                    evidence.ProposedDeliveryAt is not null ||
                    evidence.Criteria is not null)
                {
                    throw new ArgumentException(
                        "A complete negative search requires reasons and cannot contain a proposed alternative.",
                        nameof(evidence));
                }
                break;

            case AlternativePromiseSearchStatus.CompleteFeasibleAlternative:
                if (evidence.ReasonCodes.Count > 0)
                {
                    throw new ArgumentException(
                        "A complete feasible alternative cannot carry unresolved search reasons.",
                        nameof(evidence));
                }
                break;
        }
    }

    private static void ValidateReasonCodes(IReadOnlyList<string> reasonCodes, string parameterName)
    {
        if (reasonCodes.Any(code => string.IsNullOrWhiteSpace(code) || !ReasonCodePattern.IsMatch(code)) ||
            reasonCodes.Distinct(StringComparer.Ordinal).Count() != reasonCodes.Count)
        {
            throw new ArgumentException(
                "Reason codes must be canonical uppercase identifiers without duplicates.",
                parameterName);
        }
    }

    private static FeasibilityOutcomeEvaluation Resolved(
        FeasibilityOutcome outcome,
        DateTimeOffset? proposedDeliveryAt,
        IReadOnlyList<string>? reasonCodes = null) =>
        new(
            FeasibilityOutcomeEvaluationStatus.Resolved,
            outcome,
            proposedDeliveryAt,
            reasonCodes ?? []);

    private static FeasibilityOutcomeEvaluation Unresolved(string reasonCode) =>
        Unresolved([reasonCode]);

    private static FeasibilityOutcomeEvaluation Unresolved(IReadOnlyList<string> reasonCodes) =>
        new(
            FeasibilityOutcomeEvaluationStatus.Unresolved,
            null,
            null,
            reasonCodes);
}
