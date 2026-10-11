using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public enum FeasibilityCriterion
{
    Coverage,
    Distance,
    EligibleModes,
    EligibleCouriers,
    Capacity,
    ActiveRoutes,
    RouteInsertion,
    SlaFeasibility
}

public enum FeasibilityCriterionStatus
{
    Pass,
    Fail,
    Unresolved
}

public enum FeasibilityGateStatus
{
    ReadyForOutcomeEvaluation,
    Blocked,
    Unresolved
}

public sealed record FeasibilityCriterionResult(
    FeasibilityCriterion Criterion,
    FeasibilityCriterionStatus Status,
    IReadOnlyList<string> ReasonCodes);

public sealed record FeasibilityGateResult(
    FeasibilityGateStatus Status,
    IReadOnlyList<FeasibilityCriterionResult> Criteria,
    IReadOnlyList<FeasibilityCriterion> MissingCriteria,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Requires complete evidence for every E7.1 criterion before outcome evaluation may begin.
/// This gate does not determine ACCEPTED, ALTERNATIVE_PROMISE, or REJECTED.
/// </summary>
public static class FeasibilityEvidenceGate
{
    private static readonly FeasibilityCriterion[] RequiredCriteria =
    [
        FeasibilityCriterion.Coverage,
        FeasibilityCriterion.Distance,
        FeasibilityCriterion.EligibleModes,
        FeasibilityCriterion.EligibleCouriers,
        FeasibilityCriterion.Capacity,
        FeasibilityCriterion.ActiveRoutes,
        FeasibilityCriterion.RouteInsertion,
        FeasibilityCriterion.SlaFeasibility
    ];

    private static readonly Regex ReasonCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<FeasibilityCriterion> Required =>
        Array.AsReadOnly(RequiredCriteria);

    public static FeasibilityGateResult Evaluate(
        IReadOnlyCollection<FeasibilityCriterionResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        var byCriterion = new Dictionary<FeasibilityCriterion, FeasibilityCriterionResult>();
        foreach (var result in results)
        {
            if (result is null)
                throw new ArgumentException("Criterion results cannot contain null entries.", nameof(results));
            if (!Enum.IsDefined(result.Criterion))
                throw new ArgumentException("Criterion is not supported.", nameof(results));
            if (!Enum.IsDefined(result.Status))
                throw new ArgumentException("Criterion status is not supported.", nameof(results));
            if (result.ReasonCodes is null ||
                result.ReasonCodes.Any(code => string.IsNullOrWhiteSpace(code) || !ReasonCodePattern.IsMatch(code)))
            {
                throw new ArgumentException("Reason codes must be canonical uppercase identifiers.", nameof(results));
            }
            if (result.Status != FeasibilityCriterionStatus.Pass && result.ReasonCodes.Count == 0)
                throw new ArgumentException("Failed and unresolved criteria require at least one reason code.", nameof(results));
            if (!byCriterion.TryAdd(result.Criterion, result))
                throw new ArgumentException("Each criterion may be supplied only once.", nameof(results));
        }

        var ordered = RequiredCriteria
            .Where(byCriterion.ContainsKey)
            .Select(criterion => byCriterion[criterion])
            .ToArray();
        var missing = RequiredCriteria
            .Where(criterion => !byCriterion.ContainsKey(criterion))
            .ToArray();

        var failed = ordered.Any(result => result.Status == FeasibilityCriterionStatus.Fail);
        var unresolved = ordered.Any(result => result.Status == FeasibilityCriterionStatus.Unresolved);
        var status = failed
            ? FeasibilityGateStatus.Blocked
            : missing.Length > 0 || unresolved
                ? FeasibilityGateStatus.Unresolved
                : FeasibilityGateStatus.ReadyForOutcomeEvaluation;

        var reasons = ordered.SelectMany(result => result.ReasonCodes)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (failed)
            reasons.Add("FEASIBILITY_CRITERION_FAILED");
        else if (missing.Length > 0 || unresolved)
            reasons.Add("FEASIBILITY_EVIDENCE_INCOMPLETE");

        return new FeasibilityGateResult(status, ordered, missing, reasons);
    }
}
