using System.Security.Cryptography;
using System.Text;

namespace Mono.Api.Services;

/// <summary>
/// Builds stable validators for GeoJSON snapshots and active pages.
/// </summary>
public static class MonoGeoJsonCacheValidators
{
    public static string ForSnapshot(string snapshotHash) => $"\"{snapshotHash}\"";

    public static string ForActiveLayerPage(
        string kind,
        IReadOnlyList<ZoneGeometryGeoJsonFeature> features,
        bool hasMore,
        string? nextCursor)
    {
        var canonical = new StringBuilder();
        Append(canonical, kind);
        Append(canonical, hasMore ? "1" : "0");
        Append(canonical, nextCursor ?? string.Empty);
        foreach (var feature in features)
        {
            Append(canonical, feature.Properties.ZoneId);
            Append(canonical, feature.Properties.GeometryVersion);
            Append(canonical, feature.Properties.SnapshotHash);
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
        return $"\"{hash}\"";
    }

    private static void Append(StringBuilder builder, string value) =>
        builder.Append(value.Length).Append(':').Append(value);
}
