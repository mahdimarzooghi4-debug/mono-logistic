using System.Text.Json;
using Mono.Api.Persistence;
using Xunit;

namespace Mono.Api.Tests;

public sealed class OptimizationPolicyConfigurationValidatorTests
{
    [Fact]
    public void AcceptsWeightedSumWithSupportedCriteria()
    {
        using var definition = JsonDocument.Parse("""
            {
              "scoringModel": "WEIGHTED_SUM",
              "criteria": [
                { "name": "eta", "weight": 0.6, "priority": 10 },
                { "name": "cost", "weight": 0.4, "priority": 5 }
              ]
            }
            """);

        OptimizationPolicyConfigurationValidator.Validate(definition.RootElement);
    }

    [Theory]
    [InlineData("""{"scoringModel":"WEIGHTED_SUM","criteria":[]}""")]
    [InlineData("""{"scoringModel":"UNKNOWN","criteria":[{"name":"eta","weight":1,"priority":1}]}""")]
    [InlineData("""{"scoringModel":"WEIGHTED_SUM","criteria":[{"name":"unknown","weight":1,"priority":1}]}""")]
    [InlineData("""{"scoringModel":"WEIGHTED_SUM","criteria":[{"name":"eta","weight":-1,"priority":1}]}""")]
    [InlineData("""{"scoringModel":"WEIGHTED_SUM","criteria":[{"name":"eta","weight":1,"priority":0}]}""")]
    [InlineData("""{"scoringModel":"WEIGHTED_SUM","criteria":[{"name":"eta","weight":1,"priority":1},{"name":"eta","weight":0,"priority":2}]}""")]
    [InlineData("""{"scoringModel":"WEIGHTED_SUM","criteria":[{"name":"eta","weight":1,"priority":1,"extra":true}]}""")]
    public void RejectsInvalidOrUnsupportedDefinitions(string json)
    {
        using var definition = JsonDocument.Parse(json);

        Assert.Throws<ArgumentException>(() =>
            OptimizationPolicyConfigurationValidator.Validate(definition.RootElement));
    }
}
