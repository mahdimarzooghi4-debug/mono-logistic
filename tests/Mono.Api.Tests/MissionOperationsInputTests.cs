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
    [InlineData("HANA-ORDER-1", true)]
    [InlineData("x", false)]
    [InlineData("HANA\nORDER", false)]
    public void Search_query_has_bounded_non_control_input(string? value, bool expected) =>
        Assert.Equal(expected, MissionOperationsInputValidator.IsValidQuery(value));

    [Fact]
    public void Date_range_rejects_start_after_end()
    {
        var after = DateTimeOffset.Parse("2026-01-02T00:00:00Z");
        var before = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        Assert.False(MissionOperationsInputValidator.IsValidDateRange(after, before));
        Assert.True(MissionOperationsInputValidator.IsValidDateRange(before, after));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("mis_0123456789abcdef0123456789abcdef", true)]
    [InlineData("mis_not-a-valid-id", false)]
    public void Cursor_must_be_a_mission_id(string? value, bool expected) =>
        Assert.Equal(expected, MissionOperationsInputValidator.IsValidCursor(value));
}
