using Mono.Api.Services;
using Xunit;

namespace Mono.Api.Tests;

public sealed class MonoGeoJsonCacheValidatorsTests
{
    [Fact]
    public void Snapshot_validator_is_a_quoted_strong_etag()
    {
        Assert.Equal("\"abc123\"", MonoGeoJsonCacheValidators.ForSnapshot("abc123"));
    }

    [Fact]
    public void Active_layer_validator_changes_when_membership_or_snapshot_changes()
    {
        var first = Feature("zone-a", "v1", "hash-a");
        var same = Feature("zone-a", "v1", "hash-a");
        var updated = Feature("zone-a", "v2", "hash-b");

        var initial = MonoGeoJsonCacheValidators.ForActiveLayerPage(
            "OperatingZone", [first], hasMore: false, nextCursor: null);
        var repeated = MonoGeoJsonCacheValidators.ForActiveLayerPage(
            "OperatingZone", [same], hasMore: false, nextCursor: null);
        var changed = MonoGeoJsonCacheValidators.ForActiveLayerPage(
            "OperatingZone", [updated], hasMore: false, nextCursor: null);
        var nextPageState = MonoGeoJsonCacheValidators.ForActiveLayerPage(
            "OperatingZone", [same], hasMore: true, nextCursor: "next");

        Assert.Equal(initial, repeated);
        Assert.NotEqual(initial, changed);
        Assert.NotEqual(initial, nextPageState);
        Assert.StartsWith("\"", initial);
        Assert.EndsWith("\"", initial);
    }

    private static ZoneGeometryGeoJsonFeature Feature(
        string zoneId, string version, string snapshotHash) =>
        new(
            "Feature",
            new ZoneGeometryGeoJsonGeometry("Polygon", Array.Empty<double[][][]>()),
            new ZoneGeometryGeoJsonProperties(
                zoneId, "OperatingZone", version, "source", "revision", "attribution",
                "geometry-hash", snapshotHash, 4326, 5));
}
