using Mono.Api.Security;
using Xunit;

namespace Mono.Api.Tests;

public sealed class ApiKeyCredentialVerifierTests
{
    private const string Key =
        "mono_live_0123456789abcdef_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Valid_key_creates_customer_and_scope_claims_without_exposing_the_key()
    {
        var valid = ApiKeyCredentialVerifier.TryValidate(Key, Credential(), Now, out var principal);
        Assert.True(valid);
        Assert.Equal("cus_0123456789abcdef0123456789abcdef",
            principal!.FindFirst(MonoClaimTypes.CustomerId)?.Value);
        Assert.Equal("key_0123456789abcdef", principal.FindFirst("mono/api_key_id")?.Value);
        Assert.Equal("branches.read", principal.FindFirst("mono/scope")?.Value);
        Assert.DoesNotContain(principal.Claims, claim => claim.Value == Key);
    }

    [Theory]
    [InlineData("mono_live_not-a-key")]
    [InlineData("")]
    public void Malformed_key_is_rejected(string key) =>
        Assert.False(ApiKeyCredentialVerifier.TryValidate(key, Credential(), Now, out _));

    [Fact]
    public void Unknown_or_mismatched_secret_is_rejected()
    {
        var changed = Key[..^1] + "e";
        Assert.False(ApiKeyCredentialVerifier.TryValidate(changed, Credential(), Now, out _));
        Assert.False(ApiKeyCredentialVerifier.TryValidate(Key, null, Now, out _));
    }

    [Fact]
    public void Expired_revoked_inactive_or_unscoped_credentials_are_rejected()
    {
        Assert.False(ApiKeyCredentialVerifier.TryValidate(Key, Credential(expiresAt: Now), Now, out _));
        Assert.False(ApiKeyCredentialVerifier.TryValidate(Key, Credential(revokedAt: Now.AddMinutes(-1)), Now, out _));
        Assert.False(ApiKeyCredentialVerifier.TryValidate(Key, Credential(customerActive: false), Now, out _));
        Assert.False(ApiKeyCredentialVerifier.TryValidate(Key, Credential(scopes: Array.Empty<string>()), Now, out _));
    }

    [Fact]
    public void Malformed_stored_hash_is_rejected() =>
        Assert.False(ApiKeyCredentialVerifier.TryValidate(Key, Credential(secretHash: "not-a-hash"), Now, out _));

    private static ApiKeyCredential Credential(DateTimeOffset? expiresAt = null,
        DateTimeOffset? revokedAt = null, bool customerActive = true,
        IReadOnlyList<string>? scopes = null, string? secretHash = null) =>
        new("key_0123456789abcdef", "cus_0123456789abcdef0123456789abcdef",
            secretHash ?? ApiKeyCredentialVerifier.ComputeHash(Key), expiresAt ?? Now.AddDays(30),
            revokedAt, customerActive, scopes ?? ["branches.read"]);
}
