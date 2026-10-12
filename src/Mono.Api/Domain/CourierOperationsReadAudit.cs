using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record CourierOperationsReadAuditItem(
    [property: JsonPropertyName("audit_id")] string AuditId,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("courier_id")] string? CourierId,
    [property: JsonPropertyName("result_count")] int ResultCount,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record CourierOperationsReadAuditPage(
    [property: JsonPropertyName("items")] IReadOnlyList<CourierOperationsReadAuditItem> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);
