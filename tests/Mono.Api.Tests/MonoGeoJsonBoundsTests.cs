using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class MonoGeoJsonBoundsTests
{
    [Fact]
    public void Returns_collection_bounds_for_polygon_and_multipolygon_coordinates()
    {
        var features = new[]
        {
            Feature(
                "Polygon",
                new double[][][]
                {
                    [ [0, 0], [2, 0], [2, 2], [0, 2], [0, 0] ],
                    [ [0.5, 0.5], [0.5, 1], [1, 1], [0.5, 0.5] ]
                }),
            Feature(
                "MultiPolygon",
                new double[][][][]
                {
                    [ [ [4, -3], [6, -3], [6, -1], [4, -3] ] ],
                    [ [ [-2, 5], [-1, 5], [-1, 7], [-2, 5] ] ]
                })
        };

        Assert.Equal(new[] { -2d, -3d, 6d, 7d }, MonoGeoJsonBounds.FromFeatures(features));
    }

    [Fact]
    public void Returns_null_for_an_empty_collection()
    {
        Assert.Null(MonoGeoJsonBounds.FromFeatures([]));
    }

    [Theory]
    [InlineData(double.NaN, 1)]
    [InlineData(1, double.PositiveInfinity)]
    public void Rejects_non_finite_coordinates(double longitude, double latitude)
    {
        var feature = Feature("Polygon", new double[][][]
        {
            [ [longitude, latitude] ]
        });

        Assert.Throws<InvalidOperationException>(
            () => MonoGeoJsonBounds.FromFeatures([feature]));
    }

    private static ZoneGeometryGeoJsonFeature Feature(string geometryType, object coordinates) =>
        new(
            "Feature",
            new ZoneGeometryGeoJsonGeometry(geometryType, coordinates),
            new ZoneGeometryGeoJsonProperties(
                "zone-a", "OperatingZone", "v1", "source", "revision", "attribution",
                "geometry-hash", "snapshot-hash", 4326, 5));
}
