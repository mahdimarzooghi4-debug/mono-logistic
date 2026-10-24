using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mono.Api.Domain;

public sealed record WebhookOutboxEvent(
    string EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAt,
    string CorrelationId,
    string CustomerId,
    string AggregateType,
    string AggregateId,
    long Sequence,
    string DataJson,
    DateTimeOffset CreatedAt);

public static class WebhookEventPayload
{
    private static readonly IReadOnlyDictionary<string, string[]> AllowedDataFields =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["quote.issued"] = ["quote_id", "delivery_request_id", "version"],
            ["promise.committed"] = ["promise_id", "delivery_request_id", "version", "committed_delivery_at", "outcome"],
            ["promise.revised"] = ["promise_id", "delivery_request_id", "version", "committed_delivery_at", "outcome"],
            ["mission.created"] = ["mission_id", "delivery_request_id", "status", "committed_delivery_at"],
            ["mission.accepted"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"],
            ["mission.rejected"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"],
            ["courier.assigned"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"],
            ["courier.arrived_pickup"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"],
            ["mission.handed_to_courier"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"],
            ["mission.picked_up"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"],
            ["mission.in_transit"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"],
            ["mission.sla_at_risk"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"],
            ["mission.delivered"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"],
            ["mission.failed"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"],
            ["mission.cancelled"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"],
            ["mission.return_required"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"],
            ["mission.return_completed"] = ["mission_id", "delivery_request_id", "status", "sequence", "occurred_at"]
        };

    public static string Create(WebhookOutboxEvent source)
    {
        if (!AllowedDataFields.TryGetValue(source.EventType, out var allowedFields))
            throw new ArgumentException("The webhook event type is not supported.", nameof(source));

        using var data = JsonDocument.Parse(source.DataJson);
        if (data.RootElement.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Outbox event data must be a JSON object.", nameof(source));

        var safeData = new JsonObject();
        foreach (var field in allowedFields)
        {
            if (data.RootElement.TryGetProperty(field, out var value))
                safeData[field] = JsonNode.Parse(value.GetRawText());
        }

        var payload = new JsonObject
        {
            ["event_id"] = source.EventId,
            ["event_type"] = source.EventType,
            ["event_version"] = source.EventVersion,
            ["occurred_at"] = source.OccurredAt.ToUniversalTime(),
            ["correlation_id"] = source.CorrelationId,
            ["aggregate"] = new JsonObject
            {
                ["type"] = source.AggregateType,
                ["id"] = source.AggregateId,
                ["sequence"] = source.Sequence
            },
            ["data"] = safeData
        };
        return payload.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }
}
