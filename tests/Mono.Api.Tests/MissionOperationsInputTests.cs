using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class MissionOperationsInputTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("PROMISED", true)]
    [InlineData("SETTLEMENT_PENDING", true)]
    [InlineData("promised", false)]
    [InlineData("IN TRANSIT", false)]
    [InlineData("PROMISED;", false)]
    public void Status_filter_requires_canonical_uppercase_token(string? value, bool expected) =>
        Assert.Equal(expected, MissionOperationsInputValidator.IsValidStatus(value));

    [Theory]
    [InlineData(null, true)]
    [InlineData("mis_0123456789abcdef0123456789abcdef", true)]
    [InlineData("mis_not-a-valid-id", false)]
    public void Cursor_must_be_a_mission_id(string? value, bool expected) =>
        Assert.Equal(expected, MissionOperationsInputValidator.IsValidCursor(value));
}
