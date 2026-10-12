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

    private static EffectivePolicyResolutionResponse Resolution(params ResolvedPolicyVersion[] policies) =>
        new(DateTimeOffset.Parse("2026-01-01T00:00:00Z"), policies);

    private static ResolvedPolicyVersion Policy(string id, string enforcement, string definition)
    {
        using var document = JsonDocument.Parse(definition);
        return new ResolvedPolicyVersion(
            id, "TEST", "CUSTOMER", "customer-1", 1, enforcement, 0,
            null, null, document.RootElement.Clone(), 2);
    }
}
