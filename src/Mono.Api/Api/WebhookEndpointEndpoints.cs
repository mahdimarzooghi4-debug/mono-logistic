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

public static class WebhookEndpointEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpointEndpoints(this IEndpointRouteBuilder endpoints)
    {
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

        return endpoints;
    }
}
