using System.Text.Json.Serialization;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record WebhookEndpointInput(
    [property: JsonPropertyName("url")] string? Url,
    [property: JsonPropertyName("events")] IReadOnlyCollection<string>? Events);

public sealed record WebhookEndpointCreatedResponse(
    [property: JsonPropertyName("endpoint_id")] string EndpointId,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("events")] IReadOnlyList<string> Events,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("signing_secret")] string SigningSecret);

public sealed record WebhookEndpointResponse(
    [property: JsonPropertyName("endpoint_id")] string EndpointId,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("events")] IReadOnlyList<string> Events,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record WebhookEndpointListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<WebhookEndpointResponse> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public sealed record WebhookEndpointLifecycleEventResponse(
    [property: JsonPropertyName("event_id")] long EventId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt);

public sealed record WebhookEndpointLifecycleEventListResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<WebhookEndpointLifecycleEventResponse> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public sealed record WebhookEndpointSecretRotatedResponse(
    [property: JsonPropertyName("endpoint_id")] string EndpointId,
    [property: JsonPropertyName("signing_secret")] string SigningSecret,
    [property: JsonPropertyName("rotated_at")] DateTimeOffset RotatedAt);

public static class WebhookEndpointEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpointEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/webhook-endpoints", async Task<IResult> (
                ICustomerContext customer,
                PostgresWebhookEndpointRepository repository,
                int? limit,
                string? cursor,
                CancellationToken cancellationToken) =>
            {
                var pageSize = limit ?? 50;
                if (pageSize is < 1 or > 100)
                    return Results.BadRequest(new ApiErrorResponse(new ApiError("INVALID_LIMIT", "Limit must be between 1 and 100.")));

                DateTimeOffset? beforeCreatedAt = null;
                string? beforeEndpointId = null;
                if (cursor is not null)
                {
                    if (!WebhookEndpointCursor.TryDecode(cursor, out var decodedAt, out var decodedId))
                        return Results.BadRequest(new ApiErrorResponse(new ApiError("INVALID_CURSOR", "The webhook endpoint cursor is invalid.")));
                    beforeCreatedAt = decodedAt;
                    beforeEndpointId = decodedId;
                }

                var page = await repository.ListAsync(customer.CustomerId, pageSize,
                    beforeCreatedAt, beforeEndpointId, cancellationToken);
                return Results.Ok(new WebhookEndpointListResponse(page.Items.Select(ToResponse).ToArray(), page.NextCursor));
            })
            .RequireAuthorization(MonoAuthorizationPolicies.WebhooksManage);

        endpoints.MapGet("/v1/webhook-endpoints/{endpointId}", async Task<IResult> (
                string endpointId,
                ICustomerContext customer,
                PostgresWebhookEndpointRepository repository,
                CancellationToken cancellationToken) =>
            {
                var endpoint = await repository.FindAsync(customer.CustomerId, endpointId, cancellationToken);
                return endpoint is null ? Results.NotFound() : Results.Ok(ToResponse(endpoint));
            })
            .RequireAuthorization(MonoAuthorizationPolicies.WebhooksManage);

        endpoints.MapGet("/v1/webhook-endpoints/{endpointId}/audit", async Task<IResult> (
                string endpointId,
                ICustomerContext customer,
                PostgresWebhookEndpointRepository repository,
                int? limit,
                string? cursor,
                CancellationToken cancellationToken) =>
            {
                var pageSize = limit ?? 50;
                if (pageSize is < 1 or > 100)
                    return Results.BadRequest(new ApiErrorResponse(new ApiError("INVALID_LIMIT", "Limit must be between 1 and 100.")));

                long? beforeEventId = null;
                if (cursor is not null)
                {
                    if (!WebhookEndpointLifecycleCursor.TryDecode(cursor, out var decodedEventId))
                        return Results.BadRequest(new ApiErrorResponse(new ApiError("INVALID_CURSOR", "The webhook endpoint audit cursor is invalid.")));
                    beforeEventId = decodedEventId;
                }

                if (await repository.FindAsync(customer.CustomerId, endpointId, cancellationToken) is null)
                    return Results.NotFound();
                var page = await repository.ListLifecycleEventsAsync(
                    customer.CustomerId, endpointId, pageSize, beforeEventId, cancellationToken);
                return Results.Ok(new WebhookEndpointLifecycleEventListResponse(
                    page.Items.Select(item => new WebhookEndpointLifecycleEventResponse(
                        item.EventId, item.Action, item.OccurredAt)).ToArray(), page.NextCursor));
            })
            .RequireAuthorization(MonoAuthorizationPolicies.WebhooksManage);

        endpoints.MapPost("/v1/webhook-endpoints", async Task<IResult> (
                WebhookEndpointInput input,
                ICustomerContext customer,
                PostgresWebhookEndpointRepository repository,
                WebhookSecretProtector protector,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var errors = WebhookEndpointValidator.Validate(input.Url, input.Events);
                if (errors.Count > 0)
                    return Results.BadRequest(new ApiErrorResponse(new ApiError(
                        "INVALID_WEBHOOK_ENDPOINT", string.Join(" ", errors))));

                if (!protector.IsConfigured)
                    return Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Webhook endpoint registration is unavailable.",
                        detail: "Webhook secret protection is not configured.");

                var url = new Uri(input.Url!.Trim(), UriKind.Absolute).AbsoluteUri;
                var events = input.Events!.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                var secret = "whsec_" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                var createdAt = timeProvider.GetUtcNow();
                var stored = await repository.CreateAsync(
                    customer.CustomerId, url, events, protector.Protect(secret), createdAt, cancellationToken);

                return Results.Created($"/v1/webhook-endpoints/{stored.EndpointId}",
                    new WebhookEndpointCreatedResponse(
                        stored.EndpointId, stored.Url, stored.Events, stored.Status, stored.CreatedAt, secret));
            })
            .RequireAuthorization(MonoAuthorizationPolicies.WebhooksManage);

        endpoints.MapPost("/v1/webhook-endpoints/{endpointId}/disable", async Task<IResult> (
                string endpointId,
                ICustomerContext customer,
                PostgresWebhookEndpointRepository repository,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                var result = await repository.DisableAsync(customer.CustomerId, endpointId,
                    timeProvider.GetUtcNow(), cancellationToken);
                return result.Endpoint is null
                    ? Results.NotFound()
                    : Results.Ok(ToResponse(result.Endpoint));
            })
            .RequireAuthorization(MonoAuthorizationPolicies.WebhooksManage);

        endpoints.MapPost("/v1/webhook-endpoints/{endpointId}/rotate-secret", async Task<IResult> (
                string endpointId,
                ICustomerContext customer,
                PostgresWebhookEndpointRepository repository,
                WebhookSecretProtector protector,
                TimeProvider timeProvider,
                CancellationToken cancellationToken) =>
            {
                if (!protector.IsConfigured)
                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Webhook secret rotation is unavailable.",
                        detail: "Webhook secret protection is not configured.");

                var secret = "whsec_" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
                var rotatedAt = timeProvider.GetUtcNow();
                var result = await repository.RotateSecretAsync(customer.CustomerId, endpointId,
                    protector.Protect(secret), rotatedAt, cancellationToken);
                if (result.Endpoint is null)
                    return Results.NotFound();
                if (!result.Changed)
                    return Results.Conflict(new ApiErrorResponse(new ApiError(
                        "WEBHOOK_ENDPOINT_DISABLED", "A disabled webhook endpoint cannot rotate its signing secret.")));

                return Results.Ok(new WebhookEndpointSecretRotatedResponse(endpointId, secret, rotatedAt));
            })
            .RequireAuthorization(MonoAuthorizationPolicies.WebhooksManage);

        return endpoints;
    }

    private static WebhookEndpointResponse ToResponse(StoredWebhookEndpoint endpoint) =>
        new(endpoint.EndpointId, endpoint.Url, endpoint.Events, endpoint.Status, endpoint.CreatedAt);
}
