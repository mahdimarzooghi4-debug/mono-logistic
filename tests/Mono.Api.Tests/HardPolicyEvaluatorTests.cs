using System.Text.Json;
using Mono.Api.Persistence;
using Xunit;

namespace Mono.Api.Tests;

public sealed class HardPolicyEvaluatorTests
{
    [Fact]
    public void PassingConstraintsPassAndNonHardPoliciesAreIgnored()
    {
        using var facts = JsonDocument.Parse("""{"weight_kg":20,"mode":"CAR"}""");
        var response = HardPolicyEvaluator.Evaluate(Resolution(
            Policy("hard", "HARD", """{"constraints":[{"fact":"weight_kg","operator":"LTE","value":25,"reasonCode":"MAX_WEIGHT_EXCEEDED"},{"fact":"mode","operator":"IN","value":["CAR","VAN"],"reasonCode":"MODE_NOT_ALLOWED"}]}"""),
            Policy("optimization", "OPTIMIZATION", """{"constraints":[]}""")), facts.RootElement);

        Assert.True(response.Passed);
        Assert.False(response.CandidateExcluded);
        Assert.Equal(1, response.EvaluatedHardPolicyCount);
        Assert.Equal(1, response.IgnoredNonHardPolicyCount);
        Assert.Empty(response.ReasonCodes);
    }

    [Fact]
    public void FailedConstraintExcludesCandidateAndReturnsItsReasonCode()
    {
        using var facts = JsonDocument.Parse("""{"distance_km":7}""");
        var response = HardPolicyEvaluator.Evaluate(Resolution(
            Policy("hard", "HARD", """{"constraints":[{"fact":"distance_km","operator":"LTE","value":5,"reasonCode":"MAX_DISTANCE_EXCEEDED"}]}""")), facts.RootElement);

        Assert.False(response.Passed);
        Assert.True(response.CandidateExcluded);
        Assert.Equal(new[] { "MAX_DISTANCE_EXCEEDED" }, response.ReasonCodes);
    }

    [Fact]
    public void MissingFactsAndUnsupportedDefinitionsFailClosed()
    {
        using var facts = JsonDocument.Parse("""{}""");
        var response = HardPolicyEvaluator.Evaluate(Resolution(
            Policy("missing", "HARD", """{"constraints":[{"fact":"weight_kg","operator":"LTE","value":25,"reasonCode":"MAX_WEIGHT_EXCEEDED"}]}"""),
            Policy("unsupported", "HARD", """{"maxDistanceKm":5}""")), facts.RootElement);

        Assert.False(response.Passed);
        Assert.True(response.CandidateExcluded);
        Assert.Contains("POLICY_FACT_MISSING", response.ReasonCodes);
        Assert.Contains("POLICY_DEFINITION_UNSUPPORTED", response.ReasonCodes);
    }

    [Fact]
    public void NumericComparisonWithWrongFactTypeFailsClosed()
    {
        using var facts = JsonDocument.Parse("""{"weight_kg":"20"}""");
        var response = HardPolicyEvaluator.Evaluate(Resolution(
            Policy("hard", "HARD", """{"constraints":[{"fact":"weight_kg","operator":"LTE","value":25,"reasonCode":"MAX_WEIGHT_EXCEEDED"}]}""")), facts.RootElement);

        Assert.False(response.Passed);
        Assert.Contains("POLICY_DEFINITION_UNSUPPORTED", response.ReasonCodes);
    }

    [Fact]
    public void ModeCapabilityPolicyPassesWhenCandidateModeIsListed()
    {
        using var facts = JsonDocument.Parse("""{"mode":"BIKE"}""");
        var response = HardPolicyEvaluator.Evaluate(Resolution(
            Policy("modes", "HARD", """{"modes":["WALK","BIKE","CAR"]}""", "MODE_CAPABILITY")),
            facts.RootElement);

        Assert.True(response.Passed);
        Assert.False(response.CandidateExcluded);
        Assert.Empty(response.ReasonCodes);
    }

    [Fact]
    public void ModeCapabilityPolicyExcludesCandidateModeOutsideCatalog()
    {
        using var facts = JsonDocument.Parse("""{"mode":"VAN"}""");
        var response = HardPolicyEvaluator.Evaluate(Resolution(
            Policy("modes", "HARD", """{"modes":["WALK","BIKE","CAR"]}""", "MODE_CAPABILITY")),
            facts.RootElement);

        Assert.False(response.Passed);
        Assert.True(response.CandidateExcluded);
        Assert.Equal(new[] { "MODE_NOT_SUPPORTED" }, response.ReasonCodes);
    }

    [Theory]
    [InlineData("""{}""", "POLICY_FACT_MISSING")]
    [InlineData("""{"mode":5}""", "POLICY_DEFINITION_UNSUPPORTED")]
    public void ModeCapabilityPolicyFailsClosedForMissingOrInvalidModeFacts(
        string factsJson, string expectedReason)
    {
        using var facts = JsonDocument.Parse(factsJson);
        var response = HardPolicyEvaluator.Evaluate(Resolution(
            Policy("modes", "HARD", """{"modes":["WALK","BIKE","CAR"]}""", "MODE_CAPABILITY")),
            facts.RootElement);

        Assert.False(response.Passed);
        Assert.True(response.CandidateExcluded);
        Assert.Equal(new[] { expectedReason }, response.ReasonCodes);
    }

    [Fact]
    public void InvalidModeCapabilityDefinitionFailsClosedDuringEvaluation()
    {
        using var facts = JsonDocument.Parse("""{"mode":"BIKE"}""");
        var response = HardPolicyEvaluator.Evaluate(Resolution(
            Policy("modes", "HARD", """{"modes":["BIKE","BIKE"]}""", "MODE_CAPABILITY")),
            facts.RootElement);

        Assert.False(response.Passed);
        Assert.Contains("POLICY_DEFINITION_UNSUPPORTED", response.ReasonCodes);
    }

    [Fact]
    public void EligibleModePreviewIntersectsEffectiveCapabilityPolicies()
    {
        using var facts = JsonDocument.Parse("""{"distance_km":3}""");
        var response = EligibleModeEvaluator.Evaluate(Resolution(
            Policy("default-modes", "HARD", """{"modes":["WALK","BIKE","CAR"]}""", "MODE_CAPABILITY"),
            Policy("customer-modes", "HARD", """{"modes":["BIKE","CAR"]}""", "MODE_CAPABILITY")),
            facts.RootElement);

        Assert.Equal(new[] { "BIKE", "CAR" }, response.EligibleModes);
        Assert.Equal(new[] { "WALK", "BIKE", "CAR" }, response.Candidates.Select(candidate => candidate.Mode));
        Assert.False(response.Candidates[0].Eligible);
        Assert.Equal(new[] { "MODE_NOT_SUPPORTED" }, response.Candidates[0].ReasonCodes);
    }

    [Fact]
    public void EligibleModePreviewReturnsAllFourHanaPhaseOneModesFromConfiguredPolicy()
    {
        using var facts = JsonDocument.Parse("""{"weight_kg":4,"distance_km":2.5}""");
        var response = EligibleModeEvaluator.Evaluate(Resolution(
            Policy("hana-modes", "HARD",
                """{"modes":["WALK","BIKE","MOTORCYCLE","CAR"]}""",
                "MODE_CAPABILITY")),
            facts.RootElement);

        Assert.Equal(new[] { "WALK", "BIKE", "MOTORCYCLE", "CAR" }, response.EligibleModes);
        Assert.All(response.Candidates, candidate => Assert.True(candidate.Eligible));
        Assert.Empty(response.ReasonCodes);
    }

    [Fact]
    public void EligibleModePreviewAppliesOtherHardConstraintsPerCandidate()
    {
        using var facts = JsonDocument.Parse("""{"distance_km":3}""");
        var response = EligibleModeEvaluator.Evaluate(Resolution(
            Policy("modes", "HARD", """{"modes":["WALK","BIKE","CAR"]}""", "MODE_CAPABILITY"),
            Policy("no-walk", "HARD", """{"constraints":[{"fact":"mode","operator":"NE","value":"WALK","reasonCode":"WALK_NOT_ALLOWED"}]}""")),
            facts.RootElement);

        Assert.Equal(new[] { "BIKE", "CAR" }, response.EligibleModes);
        Assert.Contains("WALK_NOT_ALLOWED", response.Candidates[0].ReasonCodes);
    }

    [Fact]
    public void EligibleModePreviewFailsClosedWithoutCapabilityPolicy()
    {
        using var facts = JsonDocument.Parse("""{}""");
        var response = EligibleModeEvaluator.Evaluate(Resolution(
            Policy("other", "HARD", """{"enabled":true}""")), facts.RootElement);

        Assert.Empty(response.EligibleModes);
        Assert.Empty(response.Candidates);
        Assert.Equal(new[] { "MODE_CAPABILITY_POLICY_MISSING" }, response.ReasonCodes);
    }

    [Fact]
    public void EligibleModePreviewFailsClosedForMalformedCapabilityPolicy()
    {
        using var facts = JsonDocument.Parse("""{}""");
        var response = EligibleModeEvaluator.Evaluate(Resolution(
            Policy("modes", "HARD", """{"modes":["BIKE","BIKE"]}""", "MODE_CAPABILITY")),
            facts.RootElement);

        Assert.Empty(response.EligibleModes);
        Assert.Empty(response.Candidates);
        Assert.Equal(new[] { "POLICY_DEFINITION_UNSUPPORTED" }, response.ReasonCodes);
    }

    private static EffectivePolicyResolutionResponse Resolution(params ResolvedPolicyVersion[] policies) =>
        new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), policies);

    private static ResolvedPolicyVersion Policy(
        string id, string enforcement, string definition, string policyType = "TEST")
    {
        using var document = JsonDocument.Parse(definition);
        return new ResolvedPolicyVersion(
            id, policyType, "CUSTOMER", "customer-1", 1, enforcement, 0,
            null, null, document.RootElement.Clone(), 2);
    }
}
