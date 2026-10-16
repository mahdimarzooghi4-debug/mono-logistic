using System.Security.Cryptography;
using System.Net;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Xunit;

namespace Mono.Api.Tests;

public sealed class WebhookEndpointContractTests
{
    [Theory]
    [InlineData("https://hooks.customer.com/mono", true)]
    [InlineData("http://hooks.customer.com/mono", false)]
    [InlineData("https://hooks.customer.com:8443/mono", false)]
    [InlineData("https://localhost/mono", false)]
    [InlineData("https://127.0.0.1/hook", false)]
    [InlineData("https://service.internal/hook", false)]
    [InlineData("https://user@hooks.customer.com/hook", false)]
    [InlineData("https://hooks.customer.com/hook#fragment", false)]
    [InlineData("https://hooks.customer.com/hook?token=secret", false)]
    public void Endpoint_url_validation_is_https_and_rejects_local_or_ambiguous_targets(string url, bool valid)
    {
        var result = WebhookEndpointValidator.Validate(url, ["mission.delivered"]);

        Assert.Equal(valid, result.Count == 0);
    }

    [Fact]
    public void Event_validation_rejects_unknown_duplicate_and_empty_subscriptions()
    {
        Assert.NotEmpty(WebhookEndpointValidator.Validate("https://hooks.customer.com", ["mission.unknown"]));
        Assert.NotEmpty(WebhookEndpointValidator.Validate("https://hooks.customer.com", ["mission.delivered", "mission.delivered"]));
        Assert.NotEmpty(WebhookEndpointValidator.Validate("https://hooks.customer.com", []));
    }

    [Fact]
    public void Secret_protector_round_trips_and_uses_randomized_authenticated_ciphertext()
    {
        var key = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray());
        var protector = new WebhookSecretProtector(key);
        const string secret = "whsec_abcdef0123456789";

        var first = protector.Protect(secret);
        var second = protector.Protect(secret);

        Assert.Equal(secret, protector.Unprotect(first));
        Assert.NotEqual(first, second);
        Assert.False(first.Contains(secret, StringComparison.Ordinal));
    }

    [Fact]
    public void Secret_protector_fails_closed_for_missing_key_and_tampering()
    {
        var unconfigured = new WebhookSecretProtector(null);
        Assert.False(unconfigured.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => unconfigured.Protect("secret"));

        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var protector = new WebhookSecretProtector(key);
        var encrypted = protector.Protect("secret");
        var parts = encrypted.Split(':');
        var ciphertext = Convert.FromBase64String(parts[3]);
        ciphertext[0] ^= 0x01;
        parts[3] = Convert.ToBase64String(ciphertext);
        var tampered = string.Join(':', parts);
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect(tampered));
        var otherKey = new WebhookSecretProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        Assert.ThrowsAny<CryptographicException>(() => otherKey.Unprotect(encrypted));
    }

    [Fact]
    public void Webhook_scope_is_available_for_managed_customer_keys()
    {
        Assert.Contains("webhooks.manage", Mono.Api.Security.MonoApiKeyScopeCatalog.All);
        Assert.True(Mono.Api.Security.MonoApiKeyScopeCatalog.IsSupported("webhooks.manage"));
    }

    [Fact]
    public void Endpoint_list_cursor_round_trips_precise_tenant_ordering_key_and_rejects_invalid_values()
    {
        var createdAt = new DateTimeOffset(2026, 10, 16, 9, 25, 46, TimeSpan.Zero).AddTicks(1234560);
        const string endpointId = "whk_0123456789abcdef0123456789abcdef";
        var cursor = WebhookEndpointCursor.Encode(createdAt, endpointId);

        Assert.True(WebhookEndpointCursor.TryDecode(cursor, out var decodedAt, out var decodedId));
        Assert.Equal(createdAt, decodedAt);
        Assert.Equal(endpointId, decodedId);
        Assert.False(WebhookEndpointCursor.TryDecode("not-a-cursor", out _, out _));
        Assert.False(WebhookEndpointCursor.TryDecode(Convert.ToBase64String("1|invalid"u8.ToArray()), out _, out _));
    }

    [Fact]
    public void Delivery_history_cursor_round_trips_and_is_bound_to_filters()
    {
        var createdAt = new DateTimeOffset(2026, 10, 16, 9, 25, 46, TimeSpan.Zero).AddTicks(1234560);
        const string deliveryId = "whd_0123456789abcdef0123456789abcdef";
        var cursor = WebhookDeliveryHistoryCursor.Encode(createdAt, deliveryId,
            "whk_0123456789abcdef0123456789abcdef", "promise.committed", "SUCCEEDED");

        Assert.True(WebhookDeliveryHistoryCursor.TryDecode(cursor,
            "whk_0123456789abcdef0123456789abcdef", "promise.committed", "SUCCEEDED",
            out var decodedAt, out var decodedId));
        Assert.Equal(createdAt, decodedAt);
        Assert.Equal(deliveryId, decodedId);
        Assert.False(WebhookDeliveryHistoryCursor.TryDecode(cursor,
            "whk_0123456789abcdef0123456789abcdef", "promise.committed", "RETRYING", out _, out _));
        Assert.False(WebhookDeliveryHistoryCursor.TryDecode("not-a-cursor", null, null, null, out _, out _));
    }

    [Theory]
    [InlineData("1.1.1.1", true)]
    [InlineData("10.1.2.3", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("192.0.2.15", false)]
    [InlineData("198.19.0.1", false)]
    [InlineData("224.0.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("2001:db8::1", false)]
    [InlineData("64:ff9b::a00:1", false)]
    [InlineData("5f00::1", false)]
    [InlineData("2001:4860:4860::8888", true)]
    [InlineData("::ffff:127.0.0.1", false)]
    public void Egress_policy_accepts_only_public_unicast_addresses(string address, bool expected)
    {
        Assert.Equal(expected, WebhookPublicAddressPolicy.IsPublicInternetAddress(IPAddress.Parse(address)));
    }
}
