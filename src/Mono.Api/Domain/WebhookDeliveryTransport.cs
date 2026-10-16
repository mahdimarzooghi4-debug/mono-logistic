using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Mono.Api.Persistence;

namespace Mono.Api.Domain;

public interface IWebhookDeliveryTransport
{
    Task<WebhookDeliveryResult> SendAsync(
        WebhookDeliveryClaim claim,
        ReadOnlyMemory<byte> signingSecret,
        CancellationToken cancellationToken);
}

public sealed class WebhookHttpDeliveryTransport(HttpClient httpClient) : IWebhookDeliveryTransport
{
    public async Task<WebhookDeliveryResult> SendAsync(
        WebhookDeliveryClaim claim,
        ReadOnlyMemory<byte> signingSecret,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(claim.Url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return new WebhookDeliveryResult(null, "INVALID_TARGET");

        var body = Encoding.UTF8.GetBytes(claim.Payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(body)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8"
        };
        request.Headers.TryAddWithoutValidation("X-Mono-Event-Id", claim.EventId);
        request.Headers.TryAddWithoutValidation("X-Mono-Delivery-Id", claim.DeliveryId);
        request.Headers.TryAddWithoutValidation("X-Mono-Event-Type", claim.EventType);
        request.Headers.TryAddWithoutValidation("X-Mono-Signature", WebhookDeliverySignature.Compute(signingSecret.Span, body));

        try
        {
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return new WebhookDeliveryResult((int)response.StatusCode, null);
        }
        catch (HttpRequestException)
        {
            return new WebhookDeliveryResult(null, "NETWORK_ERROR");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new WebhookDeliveryResult(null, "TIMEOUT");
        }
    }
}

public static class WebhookDeliverySignature
{
    public static string Compute(ReadOnlySpan<byte> secret, ReadOnlySpan<byte> payload)
    {
        if (secret.IsEmpty)
            throw new ArgumentException("Webhook signing secret must not be empty.", nameof(secret));
        var signature = HMACSHA256.HashData(secret, payload);
        return "sha256=" + Convert.ToHexString(signature).ToLowerInvariant();
    }
}
