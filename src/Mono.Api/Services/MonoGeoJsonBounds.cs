namespace Mono.Api.Services;

/// <summary>
/// Calculates the RFC 7946 2D bbox order: west, south, east, north.
/// </summary>
public static class MonoGeoJsonBounds
{
    public static double[]? FromFeatures(IReadOnlyList<ZoneGeometryGeoJsonFeature> features)
    {
        double? west = null;
        double? south = null;
        double? east = null;
        double? north = null;

        foreach (var feature in features)
        {
            foreach (var position in Positions(feature.Geometry.Coordinates))
            {
                if (position.Length < 2 || !double.IsFinite(position[0]) || !double.IsFinite(position[1]))
                    throw new InvalidOperationException("GeoJSON coordinates must contain finite longitude and latitude.");

                west = west is null ? position[0] : Math.Min(west.Value, position[0]);
                south = south is null ? position[1] : Math.Min(south.Value, position[1]);
                east = east is null ? position[0] : Math.Max(east.Value, position[0]);
                north = north is null ? position[1] : Math.Max(north.Value, position[1]);
            }
        }

        return west is null ? null : [west.Value, south!.Value, east!.Value, north!.Value];
    }

    private static IEnumerable<double[]> Positions(object coordinates) =>
        coordinates switch
        {
            double[][][] polygon => polygon.SelectMany(ring => ring),
            double[][][][] multiPolygon => multiPolygon.SelectMany(polygon => polygon.SelectMany(ring => ring)),
            _ => throw new InvalidOperationException("GeoJSON coordinates must be a Polygon or MultiPolygon.")
        };
}
