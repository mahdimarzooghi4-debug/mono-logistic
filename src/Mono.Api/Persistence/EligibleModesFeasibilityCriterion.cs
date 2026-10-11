using System.Text.RegularExpressions;
using Mono.Api.Domain;

namespace Mono.Api.Persistence;

/// <summary>
/// Converts the administrator's effective-policy mode preview into one E7.1 criterion.
/// This adapter does not create a delivery decision or select a mode for execution.
/// </summary>
public static class EligibleModesFeasibilityCriterion
{
    private static readonly Regex ModeCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{0,31}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReasonCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> IncompleteEvidenceReasons = new(StringComparer.Ordinal)
    {
        "MODE_CAPABILITY_POLICY_MISSING",
        "POLICY_DEFINITION_UNSUPPORTED",
        "POLICY_FACT_MISSING"
    };

    public static FeasibilityCriterionResult Evaluate(EligibleModeEvaluationResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.EligibleModes is null || response.Candidates is null || response.ReasonCodes is null)
            throw new ArgumentException("Mode evaluation collections are required.", nameof(response));

        var candidateCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in response.Candidates)
        {
            if (candidate is null ||
                string.IsNullOrWhiteSpace(candidate.Mode) ||
                !ModeCodePattern.IsMatch(candidate.Mode) ||
                !candidateCodes.Add(candidate.Mode) ||
                candidate.ReasonCodes is null ||
                candidate.ReasonCodes.Any(code => string.IsNullOrWhiteSpace(code) || !ReasonCodePattern.IsMatch(code)) ||
                (!candidate.Eligible && candidate.ReasonCodes.Count == 0) ||
                (candidate.Eligible && candidate.ReasonCodes.Count > 0))
            {
                throw new ArgumentException("Mode candidates must contain consistent canonical evidence.", nameof(response));
            }
        }

        if (response.EligibleModes.Any(mode => string.IsNullOrWhiteSpace(mode) || !ModeCodePattern.IsMatch(mode)) ||
            response.EligibleModes.Distinct(StringComparer.Ordinal).Count() != response.EligibleModes.Count ||
            !response.EligibleModes.SequenceEqual(
                response.Candidates.Where(candidate => candidate.Eligible).Select(candidate => candidate.Mode),
                StringComparer.Ordinal))
        {
            throw new ArgumentException("Eligible modes must match the eligible candidate results.", nameof(response));
        }

        if (response.ReasonCodes.Any(code => string.IsNullOrWhiteSpace(code) || !ReasonCodePattern.IsMatch(code)) ||
            response.ReasonCodes.Distinct(StringComparer.Ordinal).Count() != response.ReasonCodes.Count)
        {
            throw new ArgumentException("Mode evaluation reason codes must be canonical and distinct.", nameof(response));
        }

        if (response.Candidates.Count > 0)
        {
            var expectedReasons = response.Candidates.SelectMany(candidate => candidate.ReasonCodes)
                .Distinct(StringComparer.Ordinal).ToArray();
            if (!response.ReasonCodes.SequenceEqual(expectedReasons, StringComparer.Ordinal))
                throw new ArgumentException("Aggregate reason codes must match candidate evidence.", nameof(response));
        }

        if (response.EligibleModes.Count > 0)
        {
            return new FeasibilityCriterionResult(
                FeasibilityCriterion.EligibleModes,
                FeasibilityCriterionStatus.Pass,
                []);
        }

        var reasons = response.ReasonCodes.Count == 0
            ? ["MODE_CANDIDATES_UNAVAILABLE"]
            : response.ReasonCodes.ToArray();
        var hasIncompleteEvidence = response.Candidates.Count == 0 ||
            reasons.Any(IncompleteEvidenceReasons.Contains);

        return new FeasibilityCriterionResult(
            FeasibilityCriterion.EligibleModes,
            hasIncompleteEvidence ? FeasibilityCriterionStatus.Unresolved : FeasibilityCriterionStatus.Fail,
            hasIncompleteEvidence
                ? reasons
                : new[] { "NO_ELIGIBLE_MODE" }.Concat(reasons).Distinct(StringComparer.Ordinal).ToArray());
    }
}
