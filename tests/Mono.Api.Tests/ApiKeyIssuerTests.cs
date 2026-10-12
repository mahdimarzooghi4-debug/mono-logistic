using Mono.Api.Security;
using Xunit;

namespace Mono.Api.Tests;

public sealed class ApiKeyIssuerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Issued_key_matches_authentication_format_and_persists_only_a_hash()
    {
        var issued = MonoApiKeyIssuer.Create(
            "cus_0123456789abcdef0123456789abcdef",
            ["branches.read", "missions.cancel"],
            Now.AddDays(30),
            Now);

        Assert.Matches(
            "^mono_live_[0-9a-f]{16}_[0-9a-f]{64}$",
            issued.Value);
        Assert.Equal("key_" + issued.Value.Substring("mono_live_".Length, 16), issued.Credential.KeyId);
        Assert.Equal(ApiKeyCredentialVerifier.ComputeHash(issued.Value), issued.Credential.SecretHash);
        Assert.DoesNotContain(issued.Value, issued.Credential.SecretHash);
        Assert.Equal(new[] { "branches.read", "missions.cancel" }, issued.Credential.Scopes);
    }

    [Fact]
    public void Issued_keys_have_independent_random_ids_and_secrets()
    {
        var first = CreateKey();
        var second = CreateKey();

        Assert.NotEqual(first.Value, second.Value);
        Assert.NotEqual(first.Credential.KeyId, second.Credential.KeyId);
        Assert.NotEqual(first.Credential.SecretHash, second.Credential.SecretHash);
    }

    [Fact]
    public void Issuer_rejects_invalid_customer_expiry_and_unknown_scope()
    {
        Assert.Throws<ArgumentException>(() =>
            MonoApiKeyIssuer.Create("hana", ["branches.read"], Now.AddDays(1), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MonoApiKeyIssuer.Create("cus_0123456789abcdef0123456789abcdef", ["branches.read"], Now, Now));
        Assert.Throws<ArgumentException>(() =>
            MonoApiKeyIssuer.Create("cus_0123456789abcdef0123456789abcdef", ["future.admin"], Now.AddDays(1), Now));
        Assert.Throws<ArgumentException>(() =>
            MonoApiKeyIssuer.Create("cus_0123456789abcdef0123456789abcdef", [], Now.AddDays(1), Now));
        Assert.Throws<ArgumentException>(() =>
            MonoApiKeyIssuer.Create("cus_0123456789abcdef0123456789abcdef", ["branches.read", " "], Now.AddDays(1), Now));
    }

    private static IssuedApiKey CreateKey() =>
        MonoApiKeyIssuer.Create(
            "cus_0123456789abcdef0123456789abcdef",
            ["branches.read"],
            Now.AddDays(30),
            Now);
}
