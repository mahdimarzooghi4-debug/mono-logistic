using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public enum SlaEvidenceCompleteness
{
    Complete,
    Incomplete,
    Unavailable
}

public sealed record SlaFeasibilityEvidence(
    string EvaluationId,
    DateTimeOffset EvaluatedAt,
    SlaEvidenceCompleteness Completeness,
    DateTimeOffset? EstimatedDeliveryAt,
    DateTimeOffset? LatestAllowedDeliveryAt,
    IReadOnlyList<string> ReasonCodes);

/// <summary>
/// Compares a caller-supplied delivery estimate with an explicit latest-allowed delivery time.
/// It does not select the SLA deadline or estimate the route itself.
/// </summary>
public static class SlaFeasibilityCriterion
{
    private static readonly Regex ReasonCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static FeasibilityCriterionResult Evaluate(SlaFeasibilityEvidence? evidence)
    {
        if (evidence is null)
        {
            return Unresolved("SLA_EVIDENCE_UNAVAILABLE");
        }

        if (string.IsNullOrWhiteSpace(evidence.EvaluationId) ||
            evidence.EvaluationId.Length > 128 ||
            evidence.EvaluationId.Any(char.IsControl) ||
            evidence.EvaluatedAt == default ||
            !Enum.IsDefined(evidence.Completeness) ||
            evidence.ReasonCodes is null ||
            (evidence.EstimatedDeliveryAt.HasValue && evidence.EstimatedDeliveryAt.Value == default) ||
            (evidence.LatestAllowedDeliveryAt.HasValue && evidence.LatestAllowedDeliveryAt.Value == default))
        {
            throw new ArgumentException("SLA feasibility evidence is malformed.", nameof(evidence));
        }

        ValidateReasonCodes(evidence.ReasonCodes, nameof(evidence));

        if (evidence.Completeness != SlaEvidenceCompleteness.Complete)
        {
            if (evidence.ReasonCodes.Count == 0)
            {
                throw new ArgumentException(
                    "Incomplete or unavailable SLA evidence requires reason codes.",
                    nameof(evidence));
            }

            return new FeasibilityCriterionResult(
                FeasibilityCriterion.SlaFeasibility,
                FeasibilityCriterionStatus.Unresolved,
                evidence.ReasonCodes);
        }

        if (evidence.ReasonCodes.Count > 0)
        {
            throw new ArgumentException(
                "Complete SLA evidence cannot carry incomplete-source reasons.",
                nameof(evidence));
        }

        if (evidence.EstimatedDeliveryAt is null || evidence.LatestAllowedDeliveryAt is null)
        {
            var reasons = new List<string>();
            if (evidence.EstimatedDeliveryAt is null)
                reasons.Add("SLA_DELIVERY_ESTIMATE_UNAVAILABLE");
            if (evidence.LatestAllowedDeliveryAt is null)
                reasons.Add("SLA_DEADLINE_UNAVAILABLE");

            return new FeasibilityCriterionResult(
                FeasibilityCriterion.SlaFeasibility,
                FeasibilityCriterionStatus.Unresolved,
                reasons);
        }

        if (evidence.EstimatedDeliveryAt.Value > evidence.LatestAllowedDeliveryAt.Value)
        {
            return new FeasibilityCriterionResult(
                FeasibilityCriterion.SlaFeasibility,
                FeasibilityCriterionStatus.Fail,
                ["SLA_DEADLINE_MISSED"]);
        }

        return new FeasibilityCriterionResult(
            FeasibilityCriterion.SlaFeasibility,
            FeasibilityCriterionStatus.Pass,
            []);
    }

    private static FeasibilityCriterionResult Unresolved(string reasonCode) =>
        new(
            FeasibilityCriterion.SlaFeasibility,
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
