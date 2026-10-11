using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record PromiseReadResponse(
    [property: JsonPropertyName("promise_id")] string PromiseId,
    [property: JsonPropertyName("delivery_request_id")] string DeliveryRequestId,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("committed_delivery_at")] DateTimeOffset CommittedDeliveryAt,
    [property: JsonPropertyName("outcome")] string Outcome);
