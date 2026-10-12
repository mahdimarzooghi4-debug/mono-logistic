using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public static class MissionLifecycleTransitions
{
    private static readonly IReadOnlyDictionary<(string Status, string EventType), string> Forward =
        new Dictionary<(string Status, string EventType), string>
        {
            [("PROMISED", "mission.searching_courier")] = "SEARCHING_COURIER",
            [("SEARCHING_COURIER", "mission.courier_assigned")] = "COURIER_ASSIGNED",
            [("COURIER_ASSIGNED", "mission.arrived_pickup")] = "ARRIVED_PICKUP",
            [("ARRIVED_PICKUP", "mission.handed_to_courier")] = "HANDED_TO_COURIER",
            [("HANDED_TO_COURIER", "mission.picked_up")] = "PICKED_UP",
            [("PICKED_UP", "mission.in_transit")] = "IN_TRANSIT",
            [("IN_TRANSIT", "mission.arrived_dropoff")] = "ARRIVED_DROPOFF",
            [("ARRIVED_DROPOFF", "mission.delivered")] = "DELIVERED"
        };

    private static readonly IReadOnlySet<string> PreHandoffStatuses = new HashSet<string>(
        ["PROMISED", "SEARCHING_COURIER", "COURIER_ASSIGNED", "ARRIVED_PICKUP"],
        StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> ActiveStatuses = new HashSet<string>(
        [
            "PROMISED", "SEARCHING_COURIER", "COURIER_ASSIGNED", "ARRIVED_PICKUP",
            "HANDED_TO_COURIER", "PICKED_UP", "IN_TRANSIT", "ARRIVED_DROPOFF"
        ],
        StringComparer.Ordinal);

    public static bool TryGetNextStatus(string currentStatus, string eventType, out string nextStatus)
    {
        if (Forward.TryGetValue((currentStatus, eventType), out nextStatus!))
            return true;

        if (eventType == "mission.cancelled" && PreHandoffStatuses.Contains(currentStatus))
        {
            nextStatus = "CANCELLED";
            return true;
        }

        if (eventType == "mission.failed" && ActiveStatuses.Contains(currentStatus))
        {
            nextStatus = "FAILED";
            return true;
        }

        nextStatus = string.Empty;
        return false;
    }

    public static IReadOnlyCollection<string> EventTypes =>
    [
        "mission.searching_courier", "mission.courier_assigned", "mission.arrived_pickup",
        "mission.handed_to_courier", "mission.picked_up", "mission.in_transit",
        "mission.arrived_dropoff", "mission.delivered", "mission.failed", "mission.cancelled"
    ];
}

public sealed record MissionLifecycleInput(
    [property: JsonPropertyName("event_type")] string? EventType,
    [property: JsonPropertyName("reason")] string? Reason);

public sealed record MissionLifecycleResponse(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("event_id")] string EventId,
    [property: JsonPropertyName("event_type")] string EventType,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public enum MissionLifecycleOutcome
{
    Applied,
    Replayed,
    Conflict,
    MissionNotFound,
    InvalidTransition
}

public sealed record MissionLifecycleResult(
    MissionLifecycleOutcome Outcome,
    MissionLifecycleResponse? Transition);
