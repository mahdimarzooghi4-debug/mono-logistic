using System.Security.Cryptography;
using Mono.Api.Domain;
using Mono.Api.Security;
using Xunit;

namespace Mono.Api.Tests;

public sealed class WebhookEndpointContractTests
{
    [Theory]
    [InlineData("https://hooks.customer.com/mono", true)]
    [InlineData("http://hooks.customer.com/mono", false)]
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
}
