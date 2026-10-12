using System.Text.Json;
using Mono.Api.Persistence;
using Xunit;

namespace Mono.Api.Tests;

public sealed class ModeCapabilityPolicyConfigurationValidatorTests
{
    [Fact]
    public void AcceptsInitialModesAndFutureModesInOneCatalog()
    {
        using var definition = JsonDocument.Parse("""
            { "modes": ["WALK", "BIKE", "CAR", "VAN", "PICKUP", "TRUCK"] }
            """);

        ModeCapabilityPolicyConfigurationValidator.Validate(definition.RootElement);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"modes":[]}""")]
    [InlineData("""{"modes":["WALK","WALK"]}""")]
    [InlineData("""{"modes":["walk"]}""")]
    [InlineData("""{"modes":["BIKE-CARGO"]}""")]
    [InlineData("""{"modes":[1]}""")]
    [InlineData("""{"modes":["CAR"],"enabled":true}""")]
    public void RejectsInvalidModeDefinitions(string json)
    {
        using var definition = JsonDocument.Parse(json);

        Assert.Throws<ArgumentException>(() =>
            ModeCapabilityPolicyConfigurationValidator.Validate(definition.RootElement));
    }
}
