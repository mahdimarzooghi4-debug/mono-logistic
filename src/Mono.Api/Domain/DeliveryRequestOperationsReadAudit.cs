using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record DeliveryRequestOperationsReadAuditItem(
    [property: JsonPropertyName("audit_id")] string AuditId,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("delivery_request_id")] string? DeliveryRequestId,
    [property: JsonPropertyName("result_count")] int ResultCount,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record DeliveryRequestOperationsReadAuditPage(
    [property: JsonPropertyName("items")] IReadOnlyList<DeliveryRequestOperationsReadAuditItem> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);
