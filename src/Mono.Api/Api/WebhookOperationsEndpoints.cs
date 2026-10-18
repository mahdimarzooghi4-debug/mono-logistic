using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Domain;

namespace Mono.Api.Api;

public static class WebhookOperationsEndpoints
{
    private static readonly HashSet<string> AuditActions = new(StringComparer.Ordinal)
    {
        "QUEUE_SUMMARY", "QUEUE_SEARCH", "DELIVERY_DETAIL", "DELIVERY_REPLAY", "DELIVERY_CANCEL", "AUDIT_HISTORY_READ", "WORKER_STATUS_READ"
    };

    private static readonly HashSet<string> Statuses = new(StringComparer.Ordinal)
    {
        "PENDING", "IN_PROGRESS", "RETRYING", "SUCCEEDED", "DEAD_LETTERED", "CANCELLED"
    };

    public static IEndpointRouteBuilder MapWebhookOperationsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/webhook-deliveries/worker-status",
                async Task<IResult> (HttpContext context, WebhookDeliveryWorkerStatus status,
                    PostgresWebhookOperationsReadService service, TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    var snapshot = status.GetSnapshot();
                    await service.RecordWorkerStatusReadAsync(timeProvider.GetUtcNow(), cancellationToken);
                    return Results.Ok(snapshot);
                })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/webhook-deliveries/summary", async Task<IResult> (
                PostgresWebhookOperationsReadService service,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var summary = await service.GetQueueSummaryAsync(timeProvider.GetUtcNow(), cancellationToken);
                return Results.Ok(summary);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/webhook-deliveries", async Task<IResult> (
                PostgresWebhookOperationsReadService service,
                TimeProvider timeProvider,
                int? limit,
                string? customer_id,
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
                    (customer_id is not null && !IsCustomerId(customer_id)) ||
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
                            normalizedStatus, out var decodedAt, out var decodedId, customer_id))
                        return Results.BadRequest(new ApiErrorResponse(new ApiError(
                            "INVALID_CURSOR", "The webhook delivery cursor is invalid for these filters.")));
                    beforeCreatedAt = decodedAt;
                    beforeDeliveryId = decodedId;
                }

                var page = await service.SearchDeliveriesAsync(pageSize, customer_id, endpoint_id,
                    normalizedEventType, normalizedStatus, beforeCreatedAt, beforeDeliveryId,
                    timeProvider.GetUtcNow(), cancellationToken);
                return Results.Ok(new WebhookDeliveryHistoryResponse(page.Items, page.NextCursor));
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/webhook-deliveries/audit", async Task<IResult> (
                PostgresWebhookOperationsReadService service,
                TimeProvider timeProvider,
                int? limit,
                string? actor_id,
                string? action,
                string? cursor,
                CancellationToken cancellationToken) =>
            {
                var pageSize = limit ?? 50;
                var normalizedActorId = string.IsNullOrWhiteSpace(actor_id) ? null : actor_id.Trim();
                var normalizedAction = string.IsNullOrWhiteSpace(action) ? null : action.Trim().ToUpperInvariant();
                if (pageSize is < 1 or > 100 ||
                    (normalizedActorId is not null && (normalizedActorId.Length > 128 ||
                        normalizedActorId.Any(char.IsControl))) ||
                    (normalizedAction is not null && !AuditActions.Contains(normalizedAction)))
                    return Results.BadRequest(new ApiErrorResponse(new ApiError(
                        "INVALID_WEBHOOK_AUDIT_FILTER", "Webhook operations audit filters are invalid.")));

                DateTimeOffset? beforeOccurredAt = null;
                string? beforeAuditId = null;
                if (cursor is not null)
                {
                    if (!WebhookOperationsAuditCursor.TryDecode(cursor, normalizedActorId, normalizedAction,
                            out var decodedAt, out var decodedId))
                        return Results.BadRequest(new ApiErrorResponse(new ApiError(
                            "INVALID_CURSOR", "The webhook operations audit cursor is invalid for these filters.")));
                    beforeOccurredAt = decodedAt;
                    beforeAuditId = decodedId;
                }

                var page = await service.ListAuditAsync(pageSize, normalizedActorId, normalizedAction,
                    beforeOccurredAt, beforeAuditId, timeProvider.GetUtcNow(), cancellationToken);
                return Results.Ok(page);
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet("/operations/v1/webhook-deliveries/{deliveryId}", async Task<IResult> (
                string deliveryId,
                PostgresWebhookOperationsReadService service,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (!IsDeliveryId(deliveryId))
                    return Results.NotFound();
                var detail = await service.GetDeliveryDetailAsync(deliveryId, timeProvider.GetUtcNow(), cancellationToken);
                return detail is null
                    ? Results.NotFound()
                    : Results.Ok(new WebhookDeliveryDetailResponse(detail.Delivery, detail.Attempts));
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/webhook-deliveries/{deliveryId}/replay", async Task<IResult> (
                string deliveryId,
                HttpRequest request,
                PostgresWebhookOperationsReadService service,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (!IsDeliveryId(deliveryId))
                    return Results.NotFound();
                var idempotencyValues = request.Headers["Idempotency-Key"];
                var idempotencyKey = idempotencyValues.Count == 1 ? idempotencyValues[0] : null;
                if (idempotencyKey is null || !IsValidIdempotencyKey(idempotencyKey))
                    return Results.BadRequest(new ApiErrorResponse(new ApiError(
                        "INVALID_IDEMPOTENCY_KEY", "A valid Idempotency-Key header is required.")));

                var result = await service.ReplayDeliveryAsync(deliveryId, idempotencyKey,
                    timeProvider.GetUtcNow(), cancellationToken);
                if (result.Delivery is not null)
                    return Results.Accepted($"/operations/v1/webhook-deliveries/{result.Delivery.DeliveryId}", result.Delivery);
                return result.ErrorCode switch
                {
                    "NOT_FOUND" => Results.NotFound(),
                    "NOT_REPLAYABLE" => Results.Conflict(new ApiErrorResponse(new ApiError(
                        "WEBHOOK_DELIVERY_NOT_REPLAYABLE", "Only dead-lettered deliveries can be replayed."))),
                    "ENDPOINT_DISABLED" => Results.Conflict(new ApiErrorResponse(new ApiError(
                        "WEBHOOK_ENDPOINT_DISABLED", "The webhook endpoint must be active to replay a delivery."))),
                    "REPLAY_LIMIT_REACHED" => Results.Conflict(new ApiErrorResponse(new ApiError(
                        "WEBHOOK_REPLAY_LIMIT_REACHED", "The delivery has reached its manual replay limit."))),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                };
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/webhook-deliveries/{deliveryId}/cancel", async Task<IResult> (
                string deliveryId,
                HttpRequest request,
                PostgresWebhookOperationsReadService service,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (!IsDeliveryId(deliveryId))
                    return Results.NotFound();
                var idempotencyValues = request.Headers["Idempotency-Key"];
                var idempotencyKey = idempotencyValues.Count == 1 ? idempotencyValues[0] : null;
                if (idempotencyKey is null || !IsValidIdempotencyKey(idempotencyKey))
                    return Results.BadRequest(new ApiErrorResponse(new ApiError(
                        "INVALID_IDEMPOTENCY_KEY", "A valid Idempotency-Key header is required.")));

                var result = await service.CancelDeliveryAsync(deliveryId, idempotencyKey,
                    timeProvider.GetUtcNow(), cancellationToken);
                return result.OutcomeCode switch
                {
                    "CANCELLED" or "ALREADY_CANCELLED" => Results.Ok(new WebhookDeliveryCancellationResponse(
                        deliveryId, result.OutcomeCode, result.AlreadyRequested)),
                    "NOT_FOUND" => Results.NotFound(),
                    "NOT_CANCELLABLE" => Results.Conflict(new ApiErrorResponse(new ApiError(
                        "WEBHOOK_DELIVERY_NOT_CANCELLABLE",
                        "Only pending or retrying deliveries without an active worker lease can be cancelled."))),
                    _ => Results.Problem(statusCode: StatusCodes.Status500InternalServerError)
                };
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }

    private static bool IsCustomerId(string value) => value.Length == 36 &&
        value.StartsWith("cus_", StringComparison.Ordinal) &&
        value[4..].All(ch => char.IsAsciiHexDigit(ch) && !char.IsUpper(ch));

    private static bool IsEndpointId(string value) => value.Length == 36 &&
        value.StartsWith("whk_", StringComparison.Ordinal) &&
        value[4..].All(ch => char.IsAsciiHexDigit(ch) && !char.IsUpper(ch));

    private static bool IsDeliveryId(string value) => value.Length == 36 &&
        value.StartsWith("whd_", StringComparison.Ordinal) &&
        value[4..].All(ch => char.IsAsciiHexDigit(ch) && !char.IsUpper(ch));

    private static bool IsValidIdempotencyKey(string value) => value.Length is > 0 and <= 128 &&
        value.All(character => character is >= '!' and <= '~');
}

public sealed record WebhookDeliveryCancellationResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("delivery_id")] string DeliveryId,
    [property: System.Text.Json.Serialization.JsonPropertyName("outcome_code")] string OutcomeCode,
    [property: System.Text.Json.Serialization.JsonPropertyName("already_requested")] bool AlreadyRequested);
