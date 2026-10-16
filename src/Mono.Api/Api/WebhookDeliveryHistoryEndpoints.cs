using System.Text.Json.Serialization;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record WebhookDeliveryHistoryResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<WebhookDeliveryHistoryItem> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public sealed record WebhookDeliveryDetailResponse(
    [property: JsonPropertyName("delivery")] WebhookDeliveryHistoryItem Delivery,
    [property: JsonPropertyName("attempts")] IReadOnlyList<WebhookDeliveryAttemptEvent> Attempts);

public static class WebhookDeliveryHistoryEndpoints
{
    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
    {
        "PENDING", "IN_PROGRESS", "RETRYING", "SUCCEEDED", "DEAD_LETTERED", "CANCELLED"
    };

    public static IEndpointRouteBuilder MapWebhookDeliveryHistoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/webhook-deliveries", async Task<IResult> (
                ICustomerContext customer,
                PostgresWebhookDeliveryHistoryRepository repository,
                int? limit,
                string? endpoint_id,
                string? event_type,
                string? status,
                string? cursor,
                CancellationToken cancellationToken) =>
            {
                var pageSize = limit ?? 50;
                var normalizedStatus = string.IsNullOrWhiteSpace(status) ? null : status.Trim().ToUpperInvariant();
                var normalizedEventType = string.IsNullOrWhiteSpace(event_type) ? null : event_type.Trim();
                if (pageSize is < 1 or > 100 ||
                    (endpoint_id is not null && !IsEndpointId(endpoint_id)) ||
                    (normalizedEventType is not null && !WebhookEventCatalog.All.Contains(normalizedEventType)) ||
                    (normalizedStatus is not null && !Statuses.Contains(normalizedStatus)))
                    return Results.BadRequest(new ApiErrorResponse(new ApiError(
                        "INVALID_WEBHOOK_DELIVERY_FILTER", "Webhook delivery filters are invalid.")));

                DateTimeOffset? beforeCreatedAt = null;
                string? beforeDeliveryId = null;
                if (cursor is not null)
                {
                    if (!WebhookDeliveryHistoryCursor.TryDecode(cursor, endpoint_id, normalizedEventType,
                            normalizedStatus, out var decodedAt, out var decodedId))
                        return Results.BadRequest(new ApiErrorResponse(new ApiError(
                            "INVALID_CURSOR", "The webhook delivery cursor is invalid for these filters.")));
                    beforeCreatedAt = decodedAt;
                    beforeDeliveryId = decodedId;
                }

                var page = await repository.ListAsync(customer.CustomerId, pageSize, endpoint_id,
                    normalizedEventType, normalizedStatus, beforeCreatedAt, beforeDeliveryId, cancellationToken);
                return Results.Ok(new WebhookDeliveryHistoryResponse(page.Items, page.NextCursor));
            })
            .RequireAuthorization(MonoAuthorizationPolicies.WebhooksManage);

        endpoints.MapGet("/v1/webhook-deliveries/{deliveryId}", async Task<IResult> (
                string deliveryId,
                ICustomerContext customer,
                PostgresWebhookDeliveryHistoryRepository repository,
                CancellationToken cancellationToken) =>
            {
                if (!IsDeliveryId(deliveryId))
                    return Results.NotFound();
                var detail = await repository.FindAsync(customer.CustomerId, deliveryId, cancellationToken);
                return detail is null
                    ? Results.NotFound()
                    : Results.Ok(new WebhookDeliveryDetailResponse(detail.Delivery, detail.Attempts));
            })
            .RequireAuthorization(MonoAuthorizationPolicies.WebhooksManage);

        return endpoints;
    }

    private static bool IsEndpointId(string value) => value.Length == 36 &&
        value.StartsWith("whk_", StringComparison.Ordinal) &&
        value[4..].All(ch => char.IsAsciiHexDigit(ch) && !char.IsUpper(ch));

    private static bool IsDeliveryId(string value) => value.Length == 36 &&
        value.StartsWith("whd_", StringComparison.Ordinal) &&
        value[4..].All(ch => char.IsAsciiHexDigit(ch) && !char.IsUpper(ch));
}
