using Mono.Api.Domain;
using Mono.Api.Persistence;
using Xunit;

namespace Mono.Api.Tests;

public sealed class EligibleModesFeasibilityCriterionTests
{
    [Fact]
    public void AnyEligibleModePassesTheCriterion()
    {
        var result = EligibleModesFeasibilityCriterion.Evaluate(Response(
            [Candidate("BIKE", true), Candidate("CAR", false, "MAX_WEIGHT_EXCEEDED")],
            ["BIKE"],
            ["MAX_WEIGHT_EXCEEDED"]));

        Assert.Equal(FeasibilityCriterion.EligibleModes, result.Criterion);
        Assert.Equal(FeasibilityCriterionStatus.Pass, result.Status);
        Assert.Empty(result.ReasonCodes);
    }

    [Fact]
    public void MissingCapabilityPolicyIsUnresolved()
    {
        var result = EligibleModesFeasibilityCriterion.Evaluate(Response(
            [],
            [],
            ["MODE_CAPABILITY_POLICY_MISSING"]));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Contains("MODE_CAPABILITY_POLICY_MISSING", result.ReasonCodes);
    }

    [Fact]
    public void UnsupportedPolicyDefinitionIsUnresolved()
    {
        var result = EligibleModesFeasibilityCriterion.Evaluate(Response(
            [],
            [],
            ["POLICY_DEFINITION_UNSUPPORTED"]));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Contains("POLICY_DEFINITION_UNSUPPORTED", result.ReasonCodes);
    }

    [Fact]
    public void MissingFactsDoNotBecomeAKnownNoModeFailure()
    {
        var result = EligibleModesFeasibilityCriterion.Evaluate(Response(
            [Candidate("BIKE", false, "POLICY_FACT_MISSING")],
            [],
            ["POLICY_FACT_MISSING"]));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.DoesNotContain("NO_ELIGIBLE_MODE", result.ReasonCodes);
    }

    [Fact]
    public void DefinitivelyExcludedCandidatesFailWithStableReason()
    {
        var result = EligibleModesFeasibilityCriterion.Evaluate(Response(
            [Candidate("BIKE", false, "MODE_NOT_SUPPORTED"), Candidate("CAR", false, "MAX_WEIGHT_EXCEEDED")],
            [],
            ["MODE_NOT_SUPPORTED", "MAX_WEIGHT_EXCEEDED"]));

        Assert.Equal(FeasibilityCriterionStatus.Fail, result.Status);
        Assert.Equal(
            new[] { "NO_ELIGIBLE_MODE", "MODE_NOT_SUPPORTED", "MAX_WEIGHT_EXCEEDED" },
            result.ReasonCodes);
    }

    [Fact]
    public void NoCandidateEvidenceIsUnresolved()
    {
        var result = EligibleModesFeasibilityCriterion.Evaluate(Response([], [], []));

        Assert.Equal(FeasibilityCriterionStatus.Unresolved, result.Status);
        Assert.Equal(new[] { "MODE_CANDIDATES_UNAVAILABLE" }, result.ReasonCodes);
    }

    [Fact]
    public void EligibleModeListMustMatchCandidateEvidence()
    {
        var response = Response([Candidate("BIKE", true)], ["CAR"], []);

        Assert.Throws<ArgumentException>(() => EligibleModesFeasibilityCriterion.Evaluate(response));
    }

    [Fact]
    public void DuplicateCandidateModesAreRejected()
    {
        var response = Response([Candidate("BIKE", false, "MODE_NOT_SUPPORTED"), Candidate("BIKE", false, "MODE_NOT_SUPPORTED")],
            [], ["MODE_NOT_SUPPORTED"]);

        Assert.Throws<ArgumentException>(() => EligibleModesFeasibilityCriterion.Evaluate(response));
    }

    [Fact]
    public void NonCanonicalReasonCodesAreRejected()
    {
        var response = Response([Candidate("BIKE", false, "missing")], [], ["missing"]);

        Assert.Throws<ArgumentException>(() => EligibleModesFeasibilityCriterion.Evaluate(response));
    }

    [Fact]
    public void CriterionCanBePassedToAggregateGateButDoesNotCompleteOtherCriteria()
    {
        var mode = EligibleModesFeasibilityCriterion.Evaluate(Response(
            [Candidate("BIKE", true)],
            ["BIKE"],
            []));
        var gate = FeasibilityEvidenceGate.Evaluate([mode]);

        Assert.Equal(FeasibilityGateStatus.Unresolved, gate.Status);
        Assert.Contains(FeasibilityCriterion.Distance, gate.MissingCriteria);
    }

    private static EligibleModeEvaluationResponse Response(
        IReadOnlyList<EligibleModeCandidateResult> candidates,
        IReadOnlyList<string> eligibleModes,
        IReadOnlyList<string> reasonCodes) =>
        new(DateTimeOffset.UnixEpoch, eligibleModes, candidates, reasonCodes);

    private static EligibleModeCandidateResult Candidate(
        string mode, bool eligible, params string[] reasonCodes) =>
        new(mode, eligible, reasonCodes);
}
