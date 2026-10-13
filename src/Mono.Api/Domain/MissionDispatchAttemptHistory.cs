using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record MissionDispatchAttempt(
    [property: JsonPropertyName("offer_id")] string OfferId,
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("zone_id")] string ZoneId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("last_action")] string? LastAction,
    [property: JsonPropertyName("last_actor_type")] string? LastActorType,
    [property: JsonPropertyName("last_reason")] string? LastReason,
    [property: JsonPropertyName("last_occurred_at")] DateTimeOffset? LastOccurredAt);

public sealed record MissionDispatchAttemptHistory(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("attempts")] IReadOnlyList<MissionDispatchAttempt> Attempts);
