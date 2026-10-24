using System.Security.Cryptography;
using System.Text;

namespace Mono.Api.Domain;

public sealed class PublicRegistrationProtector
{
    private readonly byte[] _key;
    public PublicRegistrationProtector(string? base64Key)
    {
        try { _key = string.IsNullOrWhiteSpace(base64Key) ? [] : Convert.FromBase64String(base64Key); }
        catch (FormatException) { _key = []; }
    }
    public bool IsConfigured => _key.Length == 32;
    public string Protect(string id, string plaintext)
    {
        if (!IsConfigured) throw new InvalidOperationException("Public registration encryption is not configured.");
        var nonce = RandomNumberGenerator.GetBytes(12);
        var data = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[data.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes("mono-public-registration:" + id));
        CryptographicOperations.ZeroMemory(data);
        return $"v1:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(cipher)}";
    }
    public string Unprotect(string id, string value)
    {
        if (!IsConfigured) throw new InvalidOperationException("Public registration encryption is not configured.");
        var parts = value.Split(':');
        if (parts.Length != 4 || parts[0] != "v1") throw new CryptographicException("Invalid protected registration payload.");
        var nonce = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var cipher = Convert.FromBase64String(parts[3]);
        if (nonce.Length != 12 || tag.Length != 16) throw new CryptographicException("Invalid protected registration dimensions.");
        var data = new byte[cipher.Length];
        using var aes = new AesGcm(_key, tag.Length);
        try
        {
            aes.Decrypt(nonce, cipher, tag, data, Encoding.UTF8.GetBytes("mono-public-registration:" + id));
            return new UTF8Encoding(false, true).GetString(data);
        }
        finally { CryptographicOperations.ZeroMemory(data); }
    }
    public string Digest(string value)
    {
        if (!IsConfigured) throw new InvalidOperationException("Public registration encryption is not configured.");
        return Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}