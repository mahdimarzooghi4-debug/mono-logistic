using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class MonoIfNoneMatchTests
{
    [Theory]
    [InlineData("\"current\"", "\"current\"")]
    [InlineData("W/\"current\"", "\"current\"")]
    [InlineData("\"other\", W/\"current\"", "\"current\"")]
    [InlineData("  W/\"current\"  ", "\"current\"")]
    [InlineData("*", "\"current\"")]
    [InlineData("\"comma,inside\", \"current\"", "\"current\"")]
    public void Matches_valid_entity_tag_lists_using_weak_comparison(
        string header, string currentTag)
    {
        Assert.True(MonoIfNoneMatch.Matches(header, currentTag));
    }

    [Theory]
    [InlineData("\"other\"", "\"current\"")]
    [InlineData("W/\"other\"", "\"current\"")]
    [InlineData("\"current\",", "\"current\"")]
    [InlineData("*, \"current\"", "\"current\"")]
    [InlineData("\"unterminated", "\"current\"")]
    [InlineData("current", "\"current\"")]
    public void Rejects_non_matching_or_malformed_entity_tag_lists(
        string header, string currentTag)
    {
        Assert.False(MonoIfNoneMatch.Matches(header, currentTag));
    }

    [Fact]
    public void Entity_tag_comparison_is_case_sensitive()
    {
        Assert.False(MonoIfNoneMatch.Matches("\"Current\"", "\"current\""));
    }
}
