using System.Security.Cryptography;
using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class PublicRegistrationProtectorTests
{
    [Fact]
    public void ProtectsPayloadAndBindsItToRequestId()
    {
        var protector = new PublicRegistrationProtector(Convert.ToBase64String(new byte[32]));
        var ciphertext = protector.Protect("reg_123", "{\"phone\":\"09121234567\"}");
        Assert.DoesNotContain("09121234567", ciphertext);
        Assert.Equal("{\"phone\":\"09121234567\"}", protector.Unprotect("reg_123", ciphertext));
        Assert.ThrowsAny<CryptographicException>(() => protector.Unprotect("reg_456", ciphertext));
    }

    [Fact]
    public void InvalidKeyDisablesProtection()
    {
        var protector = new PublicRegistrationProtector("not-a-base64-key");
        Assert.False(protector.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => protector.Protect("reg_123", "payload"));
    }
}