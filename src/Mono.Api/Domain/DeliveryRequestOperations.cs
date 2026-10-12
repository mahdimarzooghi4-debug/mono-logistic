using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public sealed record DeliveryRequestOperationsItem(
    [property: JsonPropertyName("delivery_request_id")] string DeliveryRequestId,
    [property: JsonPropertyName("customer_id")] string CustomerId,
    [property: JsonPropertyName("branch_id")] string BranchId,
    [property: JsonPropertyName("external_reference")] string ExternalReference,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("mission_id")] string? MissionId,
    [property: JsonPropertyName("mission_status")] string? MissionStatus,
    [property: JsonPropertyName("committed_delivery_at")] DateTimeOffset? CommittedDeliveryAt);

public sealed record DeliveryRequestOperationsPage(
    [property: JsonPropertyName("items")] IReadOnlyList<DeliveryRequestOperationsItem> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public static class DeliveryRequestOperationsInputValidator
{
    private static readonly Regex StatusPattern = new(
        @"\ARECEIVED\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CustomerIdPattern = new(
        @"\Acus_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BranchIdPattern = new(
        @"\Abr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsValidStatus(string? status) =>
        status is null || StatusPattern.IsMatch(status);

    public static bool IsValidQuery(string? query) =>
        query is null ||
        (query.Length is >= 2 and <= 128 &&
         string.Equals(query, query.Trim(), StringComparison.Ordinal) &&
         !query.Any(char.IsControl));

    public static bool IsValidDateRange(DateTimeOffset? createdAfter, DateTimeOffset? createdBefore) =>
        createdAfter is null || createdBefore is null || createdAfter <= createdBefore;

    public static bool IsValidCustomerId(string? customerId) =>
        customerId is null || CustomerIdPattern.IsMatch(customerId);

    public static bool IsValidBranchId(string? branchId) =>
        branchId is null || BranchIdPattern.IsMatch(branchId);

    public static bool IsValidCursor(string? cursor) =>
        cursor is null || DeliveryRequestInputValidator.IsValidDeliveryRequestId(cursor);
}
