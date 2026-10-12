using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public enum CourierCandidateSetStatus
{
    Complete,
    Incomplete,
    Unavailable
}

public enum CourierEligibilityStatus
{
    Eligible,
    Excluded,
    Unresolved
}

public sealed record CourierEligibilityCandidateResult(
    string CandidateId,
    CourierEligibilityStatus Status,
    IReadOnlyList<string> ReasonCodes);

public sealed record CourierEligibilityEvidenceSet(
    CourierCandidateSetStatus CandidateSetStatus,
    IReadOnlyCollection<CourierEligibilityCandidateResult> Candidates,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Evaluates whether supplied courier candidate evidence proves at least one eligible courier.
/// It does not discover couriers or make an operational delivery decision.
/// </summary>
public static class EligibleCourierFeasibilityCriterion
{
    private static readonly Regex ReasonCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static FeasibilityCriterionResult Evaluate(CourierEligibilityEvidenceSet evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (!Enum.IsDefined(evidence.CandidateSetStatus))
            throw new ArgumentException("Candidate set status is not supported.", nameof(evidence));
        if (evidence.Candidates is null || evidence.ReasonCodes is null)
            throw new ArgumentException("Courier evidence collections are required.", nameof(evidence));

        ValidateReasonCodes(evidence.ReasonCodes, nameof(evidence));
        if (evidence.CandidateSetStatus != CourierCandidateSetStatus.Complete &&
            evidence.ReasonCodes.Count == 0)
        {
            throw new ArgumentException("Incomplete or unavailable candidate sets require a reason code.", nameof(evidence));
        }
        if (evidence.CandidateSetStatus == CourierCandidateSetStatus.Unavailable &&
            evidence.Candidates.Count != 0)
        {
            throw new ArgumentException("Unavailable candidate sets cannot contain candidate results.", nameof(evidence));
        }

        var candidateIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in evidence.Candidates)
        {
            if (candidate is null ||
                string.IsNullOrWhiteSpace(candidate.CandidateId) ||
                candidate.CandidateId.Length > 128 ||
                candidate.CandidateId.Any(char.IsControl) ||
                !candidateIds.Add(candidate.CandidateId) ||
                !Enum.IsDefined(candidate.Status) ||
                candidate.ReasonCodes is null)
            {
                throw new ArgumentException("Courier candidate evidence is malformed.", nameof(evidence));
            }

            ValidateReasonCodes(candidate.ReasonCodes, nameof(evidence));
            if (candidate.Status == CourierEligibilityStatus.Eligible && candidate.ReasonCodes.Count > 0)
                throw new ArgumentException("Eligible candidates cannot carry exclusion or unresolved reasons.", nameof(evidence));
            if (candidate.Status != CourierEligibilityStatus.Eligible && candidate.ReasonCodes.Count == 0)
                throw new ArgumentException("Excluded or unresolved candidates require reason codes.", nameof(evidence));
        }

        if (evidence.Candidates.Any(candidate => candidate.Status == CourierEligibilityStatus.Eligible))
        {
            return new FeasibilityCriterionResult(
                FeasibilityCriterion.EligibleCouriers,
                FeasibilityCriterionStatus.Pass,
                []);
        }

        var reasons = evidence.ReasonCodes
            .Concat(evidence.Candidates.SelectMany(candidate => candidate.ReasonCodes))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var completeAndDefinitive =
            evidence.CandidateSetStatus == CourierCandidateSetStatus.Complete &&
            evidence.Candidates.All(candidate => candidate.Status == CourierEligibilityStatus.Excluded);

        if (completeAndDefinitive)
        {
            return new FeasibilityCriterionResult(
                FeasibilityCriterion.EligibleCouriers,
                FeasibilityCriterionStatus.Fail,
                new[] { "NO_ELIGIBLE_COURIER" }.Concat(reasons).Distinct(StringComparer.Ordinal).ToArray());
        }

        if (reasons.Length == 0)
            reasons = ["COURIER_ELIGIBILITY_UNRESOLVED"];

        return new FeasibilityCriterionResult(
            FeasibilityCriterion.EligibleCouriers,
            FeasibilityCriterionStatus.Unresolved,
            reasons);
    }

    private static void ValidateReasonCodes(IReadOnlyList<string> reasonCodes, string parameterName)
    {
        if (reasonCodes.Any(code => string.IsNullOrWhiteSpace(code) || !ReasonCodePattern.IsMatch(code)) ||
            reasonCodes.Distinct(StringComparer.Ordinal).Count() != reasonCodes.Count)
        {
            throw new ArgumentException("Reason codes must be canonical uppercase identifiers without duplicates.", parameterName);
        }
    }
}
