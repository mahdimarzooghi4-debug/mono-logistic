using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public enum RouteInsertionEvidenceCompleteness
{
    Complete,
    Incomplete,
    Unavailable
}

public enum RouteInsertionFeasibility
{
    Feasible,
    Infeasible,
    Unresolved
}

public sealed record RouteInsertionEvidence(
    string EvaluationId,
    DateTimeOffset EvaluatedAt,
    RouteInsertionEvidenceCompleteness Completeness,
    RouteInsertionFeasibility Feasibility,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Maps an explicitly supplied, request-wide route-insertion evaluation to E7.1 evidence.
/// It validates the evidence envelope but does not run or verify a route simulator.
/// </summary>
public static class RouteInsertionFeasibilityCriterion
{
    private static readonly Regex ReasonCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static FeasibilityCriterionResult Evaluate(RouteInsertionEvidence? evidence)
    {
        if (evidence is null)
        {
            return Unresolved("ROUTE_INSERTION_EVIDENCE_UNAVAILABLE");
        }

        if (string.IsNullOrWhiteSpace(evidence.EvaluationId) ||
            evidence.EvaluationId.Length > 128 ||
            evidence.EvaluationId.Any(char.IsControl) ||
            evidence.EvaluatedAt == default ||
            !Enum.IsDefined(evidence.Completeness) ||
            !Enum.IsDefined(evidence.Feasibility) ||
            evidence.ReasonCodes is null)
        {
            throw new ArgumentException("Route insertion evidence is malformed.", nameof(evidence));
        }

        ValidateReasonCodes(evidence.ReasonCodes, nameof(evidence));

        if (evidence.Completeness == RouteInsertionEvidenceCompleteness.Complete)
        {
            return evidence.Feasibility switch
            {
                RouteInsertionFeasibility.Feasible when evidence.ReasonCodes.Count == 0 =>
                    new FeasibilityCriterionResult(
                        FeasibilityCriterion.RouteInsertion,
                        FeasibilityCriterionStatus.Pass,
                        []),
                RouteInsertionFeasibility.Infeasible when evidence.ReasonCodes.Count > 0 =>
                    new FeasibilityCriterionResult(
                        FeasibilityCriterion.RouteInsertion,
                        FeasibilityCriterionStatus.Fail,
                        new[] { "ROUTE_INSERTION_NOT_FEASIBLE" }
                            .Concat(evidence.ReasonCodes)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray()),
                _ => throw new ArgumentException(
                    "Complete evidence must have a definitive feasibility result and matching reason codes.",
                    nameof(evidence))
            };
        }

        if (evidence.Feasibility != RouteInsertionFeasibility.Unresolved ||
            evidence.ReasonCodes.Count == 0)
        {
            throw new ArgumentException(
                "Incomplete or unavailable evidence must be unresolved and include reason codes.",
                nameof(evidence));
        }

        return new FeasibilityCriterionResult(
            FeasibilityCriterion.RouteInsertion,
            FeasibilityCriterionStatus.Unresolved,
            evidence.ReasonCodes);
    }

    private static FeasibilityCriterionResult Unresolved(string reasonCode) =>
        new(
            FeasibilityCriterion.RouteInsertion,
            FeasibilityCriterionStatus.Unresolved,
            [reasonCode]);

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
}
