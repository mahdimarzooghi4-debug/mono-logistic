using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record CourierCapacityBucket(
    [property: JsonPropertyName("from")] DateTimeOffset From,
    [property: JsonPropertyName("to")] DateTimeOffset To,
    [property: JsonPropertyName("planned_courier_count")] int PlannedCourierCount);

public sealed record CourierCapacityProjection(
    [property: JsonPropertyName("zone_id")] string ZoneId,
    [property: JsonPropertyName("bucket_minutes")] int BucketMinutes,
    [property: JsonPropertyName("buckets")] IReadOnlyList<CourierCapacityBucket> Buckets);
