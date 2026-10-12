using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public sealed record MissionOperationsItem(
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("customer_id")] string CustomerId,
    [property: JsonPropertyName("delivery_request_id")] string DeliveryRequestId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("committed_delivery_at")] DateTimeOffset CommittedDeliveryAt,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record MissionOperationsPage(
    [property: JsonPropertyName("items")] IReadOnlyList<MissionOperationsItem> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public static class MissionOperationsInputValidator
{
    private static readonly Regex StatusPattern = new(
        @"\A[A-Z][A-Z0-9_]{0,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsValidStatus(string? status) =>
        status is null || StatusPattern.IsMatch(status);

    public static bool IsValidQuery(string? query) =>
        query is null ||
        (query.Length is >= 2 and <= 128 &&
         string.Equals(query, query.Trim(), StringComparison.Ordinal) &&
         !query.Any(char.IsControl));

    public static bool IsValidDateRange(DateTimeOffset? createdAfter, DateTimeOffset? createdBefore) =>
        createdAfter is null || createdBefore is null || createdAfter <= createdBefore;

    public static bool IsValidCursor(string? cursor) =>
        cursor is null || MissionHistoryInputValidator.IsValidMissionId(cursor);
}
