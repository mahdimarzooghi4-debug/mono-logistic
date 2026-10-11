using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class DistanceConstraintEvaluatorTests
{
    [Fact]
    public void Route_distance_at_limit_passes()
    {
        var result = DistanceConstraintEvaluator.Evaluate(5000, 5000);

        Assert.Equal(DistanceConstraintStatus.Pass, result.Status);
        Assert.Equal(5000, result.RouteDistanceMeters);
        Assert.Null(result.ReasonCode);
    }

    [Fact]
    public void Route_distance_over_limit_fails_with_reason_code()
    {
        var result = DistanceConstraintEvaluator.Evaluate(5000.1, 5000);

        Assert.Equal(DistanceConstraintStatus.Fail, result.Status);
        Assert.Equal("MAX_DISTANCE_EXCEEDED", result.ReasonCode);
    }

    [Fact]
    public void Missing_route_distance_remains_unresolved()
    {
        var result = DistanceConstraintEvaluator.Evaluate(null, 5000);

        Assert.Equal(DistanceConstraintStatus.Unresolved, result.Status);
        Assert.Null(result.RouteDistanceMeters);
        Assert.Equal("ROUTE_DISTANCE_UNAVAILABLE", result.ReasonCode);
    }

    [Fact]
    public void Zero_distance_and_zero_limit_pass()
    {
        var result = DistanceConstraintEvaluator.Evaluate(0, 0);

        Assert.Equal(DistanceConstraintStatus.Pass, result.Status);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void Maximum_distance_must_be_finite_and_non_negative(double maximumDistanceMeters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DistanceConstraintEvaluator.Evaluate(0, maximumDistanceMeters));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    public void Route_distance_must_be_finite_and_non_negative(double routeDistanceMeters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DistanceConstraintEvaluator.Evaluate(routeDistanceMeters, 5000));
    }
}
