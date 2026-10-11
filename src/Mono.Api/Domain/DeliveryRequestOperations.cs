using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public sealed record DeliveryRequestOperationsItem(
    [property: JsonPropertyName("delivery_request_id")] string DeliveryRequestId,
    [property: JsonPropertyName("customer_id")] string CustomerId,
    [property: JsonPropertyName("branch_id")] string BranchId,
    [property: JsonPropertyName("external_reference")] string ExternalReference,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record DeliveryRequestOperationsPage(
    [property: JsonPropertyName("items")] IReadOnlyList<DeliveryRequestOperationsItem> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public static class DeliveryRequestOperationsInputValidator
{
    private static readonly Regex StatusPattern = new(
        @"\ARECEIVED\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

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
        cursor is null || DeliveryRequestInputValidator.IsValidDeliveryRequestId(cursor);
}
