using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record CourierOperationsTransitionView(
    [property: JsonPropertyName("transition_id")] string TransitionId,
    [property: JsonPropertyName("from_status")] string FromStatus,
    [property: JsonPropertyName("to_status")] string ToStatus,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonPropertyName("evidence_reference")] string EvidenceReference,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record CourierOperationsSnapshot(
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("mode")] string Mode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("preferred_zone_ids")] IReadOnlyList<string> PreferredZoneIds,
    [property: JsonPropertyName("approved_zone_ids")] IReadOnlyList<string> ApprovedZoneIds,
    [property: JsonPropertyName("primary_zone_id")] string? PrimaryZoneId,
    [property: JsonPropertyName("created_by")] string CreatedBy,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("trust_transitions")] IReadOnlyList<CourierOperationsTransitionView> TrustTransitions);

public enum CourierOperationsWriteOutcome
{
    Applied,
    Replayed,
    Conflict,
    CourierNotFound,
    InvalidTransition,
    InvalidZone
}

public sealed record CourierOperationsWriteResult(
    CourierOperationsWriteOutcome Outcome,
    CourierOperationsSnapshot? Snapshot);
