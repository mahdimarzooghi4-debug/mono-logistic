using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record PromiseVersionReadResponse(
    [property: JsonPropertyName("promise_id")] string PromiseId,
    [property: JsonPropertyName("delivery_request_id")] string DeliveryRequestId,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("committed_delivery_at")] DateTimeOffset CommittedDeliveryAt,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("recorded_at")] DateTimeOffset RecordedAt);

public sealed record PromiseVersionPageResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<PromiseVersionReadResponse> Items,
    [property: JsonPropertyName("next_before_version")] int? NextBeforeVersion);
