using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record MissionOperationsReadAuditItem(
    [property: JsonPropertyName("audit_id")] string AuditId,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("mission_id")] string? MissionId,
    [property: JsonPropertyName("result_count")] int ResultCount,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record MissionOperationsReadAuditPage(
    [property: JsonPropertyName("items")] IReadOnlyList<MissionOperationsReadAuditItem> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);
