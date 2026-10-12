using Mono.Api.Domain;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using Xunit;

namespace Mono.Api.Tests;

public sealed class ZoneGeometryBoundaryTests
{
    private static readonly GeometryFactory Factory = new(new PrecisionModel(), 4326);

    [Fact]
    public void Classifies_inside_outside_and_boundary_points()
    {
        var boundary = Create("zone-a", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");

        Assert.Equal(ZonePointRelation.Inside, boundary.Classify(new GeoCoordinate(5, 5)));
        Assert.Equal(ZonePointRelation.Outside, boundary.Classify(new GeoCoordinate(15, 5)));
        Assert.Equal(ZonePointRelation.Boundary, boundary.Classify(new GeoCoordinate(0, 5)));
    }

    [Fact]
    public void Treats_polygon_holes_as_outside()
    {
        var boundary = Create(
            "zone-a",
            "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (3 3, 7 3, 7 7, 3 7, 3 3))");

        Assert.Equal(ZonePointRelation.Inside, boundary.Classify(new GeoCoordinate(2, 2)));
        Assert.Equal(ZonePointRelation.Outside, boundary.Classify(new GeoCoordinate(5, 5)));
        Assert.Equal(ZonePointRelation.Boundary, boundary.Classify(new GeoCoordinate(3, 5)));
    }

    [Fact]
    public void Supports_multipolygon_boundaries()
    {
        var boundary = Create(
            "zone-a",
            "MULTIPOLYGON (((0 0, 2 0, 2 2, 0 2, 0 0)), ((5 5, 7 5, 7 7, 5 7, 5 5)))");

        Assert.Equal(ZonePointRelation.Inside, boundary.Classify(new GeoCoordinate(1, 1)));
        Assert.Equal(ZonePointRelation.Inside, boundary.Classify(new GeoCoordinate(6, 6)));
        Assert.Equal(ZonePointRelation.Outside, boundary.Classify(new GeoCoordinate(3, 3)));
    }

    [Fact]
    public void Requires_a_valid_non_empty_wgs84_polygon()
    {
        Assert.Throws<ArgumentException>(() => Create(
            "bowtie",
            "POLYGON ((0 0, 2 2, 0 2, 2 0, 0 0))"));

        var wrongSrid = ReadWkt("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", 3857);
        Assert.Throws<ArgumentException>(() => ZoneGeometryBoundary.Create(
            "zone-a", ZoneKind.OperatingZone, "v1", "osm", "r1", "© OpenStreetMap contributors", wrongSrid));

        var outOfRange = Factory.CreatePolygon(new[]
        {
            new Coordinate(181, 0), new Coordinate(182, 0),
            new Coordinate(182, 1), new Coordinate(181, 1),
            new Coordinate(181, 0)
        });
        Assert.Throws<ArgumentException>(() => ZoneGeometryBoundary.Create(
            "zone-a", ZoneKind.OperatingZone, "v1", "osm", "r1", "© OpenStreetMap contributors", outOfRange));

        var line = new WKTReader(Factory).Read("LINESTRING (0 0, 1 1)");
        Assert.Throws<ArgumentException>(() => ZoneGeometryBoundary.Create(
            "zone-a", ZoneKind.OperatingZone, "v1", "osm", "r1", "© OpenStreetMap contributors", line));
    }

    [Fact]
    public void Rejects_antimeridian_crossing_boundaries()
    {
        var geometry = ReadWkt("POLYGON ((170 0, -170 0, -170 10, 170 10, 170 0))");

        Assert.Throws<ArgumentException>(() => ZoneGeometryBoundary.Create(
            "zone-a", ZoneKind.OperatingZone, "v1", "osm", "r1", "© OpenStreetMap contributors", geometry));
    }

    [Fact]
    public void Requires_versioned_source_and_attribution_metadata()
    {
        var geometry = Polygon("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");

        Assert.Throws<ArgumentException>(() => ZoneGeometryBoundary.Create(
            "zone-a", ZoneKind.OperatingZone, " ", "osm", "r1", "© OpenStreetMap contributors", geometry));
        Assert.Throws<ArgumentException>(() => ZoneGeometryBoundary.Create(
            "zone-a", ZoneKind.OperatingZone, "v1", "osm", "r1", " ", geometry));
    }

    [Fact]
    public void Resolves_a_unique_zone_and_keeps_hierarchy_levels_separate()
    {
        var boundaries = new[]
        {
            Create("city", ZoneKind.City, "POLYGON ((-10 -10, 20 -10, 20 20, -10 20, -10 -10))"),
            Create("zone-a", ZoneKind.OperatingZone, "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))"),
            Create("micro-a", ZoneKind.MicroZone, "POLYGON ((2 2, 4 2, 4 4, 2 4, 2 2))")
        };

        var result = ZoneGeometryResolver.Resolve(
            boundaries, ZoneKind.OperatingZone, new GeoCoordinate(3, 3));

        Assert.Equal(ZonePointResolutionStatus.Matched, result.Status);
        Assert.Equal("zone-a", result.ZoneId);
        Assert.Equal(new[] { "zone-a" }, result.CandidateZoneIds);
    }

    [Fact]
    public void Does_not_silently_choose_between_overlapping_zones()
    {
        var boundaries = new[]
        {
            Create("zone-a", "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))"),
            Create("zone-b", "POLYGON ((5 5, 15 5, 15 15, 5 15, 5 5))")
        };

        var result = ZoneGeometryResolver.Resolve(
            boundaries, ZoneKind.OperatingZone, new GeoCoordinate(7, 7));

        Assert.Equal(ZonePointResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.ZoneId);
        Assert.Equal(new[] { "zone-a", "zone-b" }, result.CandidateZoneIds);
    }

    [Fact]
    public void Reports_shared_border_as_ambiguous_and_single_edge_as_boundary()
    {
        var boundaries = new[]
        {
            Create("zone-a", "POLYGON ((0 0, 5 0, 5 5, 0 5, 0 0))"),
            Create("zone-b", "POLYGON ((5 0, 10 0, 10 5, 5 5, 5 0))")
        };

        var shared = ZoneGeometryResolver.Resolve(
            boundaries, ZoneKind.OperatingZone, new GeoCoordinate(2.5, 5));
        var singleEdge = ZoneGeometryResolver.Resolve(
            boundaries.Take(1).ToArray(), ZoneKind.OperatingZone, new GeoCoordinate(2.5, 0));

        Assert.Equal(ZonePointResolutionStatus.Ambiguous, shared.Status);
        Assert.Equal(new[] { "zone-a", "zone-b" }, shared.CandidateZoneIds);
        Assert.Equal(ZonePointResolutionStatus.Boundary, singleEdge.Status);
        Assert.Null(singleEdge.ZoneId);
    }

    [Fact]
    public void Reports_outside_when_no_zone_matches()
    {
        var result = ZoneGeometryResolver.Resolve(
            new[] { Create("zone-a", "POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))") },
            ZoneKind.OperatingZone,
            new GeoCoordinate(20, 20));

        Assert.Equal(ZonePointResolutionStatus.Outside, result.Status);
        Assert.Empty(result.CandidateZoneIds);
    }

    [Fact]
    public void Rejects_duplicate_zone_ids_within_resolution_level()
    {
        var boundaries = new[]
        {
            Create("zone-a", "POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))"),
            Create("zone-a", "POLYGON ((4 4, 6 4, 6 6, 4 6, 4 4))")
        };

        Assert.Throws<ArgumentException>(() => ZoneGeometryResolver.Resolve(
            boundaries, ZoneKind.OperatingZone, new GeoCoordinate(1, 1)));
    }

    [Fact]
    public void Requires_valid_input_location()
    {
        var boundary = Create("zone-a", "POLYGON ((0 0, 2 0, 2 2, 0 2, 0 0))");

        Assert.Throws<ArgumentException>(() => boundary.Classify(new GeoCoordinate(double.NaN, 1)));
        Assert.Throws<ArgumentException>(() => ZoneGeometryResolver.Resolve(
            new[] { boundary }, ZoneKind.OperatingZone, new GeoCoordinate(100, 100)));
    }

    private static ZoneGeometryBoundary Create(
        string zoneId,
        string wkt) => Create(zoneId, ZoneKind.OperatingZone, wkt);

    private static ZoneGeometryBoundary Create(
        string zoneId,
        ZoneKind kind,
        string wkt) =>
        ZoneGeometryBoundary.Create(
            zoneId,
            kind,
            "geometry-v1",
            "openstreetmap",
            "dataset-revision-1",
            "© OpenStreetMap contributors",
            PolygonOrMultiPolygon(wkt));

    private static Geometry PolygonOrMultiPolygon(string wkt) => ReadWkt(wkt);

    private static Polygon Polygon(string wkt) => (Polygon)ReadWkt(wkt);

    private static Geometry ReadWkt(string wkt, int srid = 4326)
    {
        var geometry = new WKTReader(Factory).Read(wkt);
        geometry.SRID = srid;
        return geometry;
    }
}
