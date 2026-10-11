using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record MissionConfirmationInput(
    [property: JsonPropertyName("quote_id")] string? QuoteId,
    [property: JsonPropertyName("promise_version")] int PromiseVersion);

public sealed record MissionConfirmationResponse(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("delivery_request_id")] string DeliveryRequestId,
    [property: JsonPropertyName("quote_id")] string QuoteId,
    [property: JsonPropertyName("quote_version")] int QuoteVersion,
    [property: JsonPropertyName("promise_id")] string PromiseId,
    [property: JsonPropertyName("promise_version")] int PromiseVersion,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("committed_delivery_at")] DateTimeOffset CommittedDeliveryAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public enum MissionConfirmationOutcome
{
    Created,
    Replayed,
    Conflict,
    QuoteNotFound,
    PromiseNotFound,
    DeliveryRequestNotFound,
    AlreadyConfirmed
}

public sealed record MissionConfirmationResult(
    MissionConfirmationOutcome Outcome,
    MissionConfirmationResponse? Mission);
