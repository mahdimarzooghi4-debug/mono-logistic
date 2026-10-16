using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public sealed class CourierPayoutIbanProtector
{
    private static readonly Regex IranianIbanPattern = new(
        @"\AIR[0-9]{24}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private readonly byte[] _key;

    public CourierPayoutIbanProtector(string? base64Key)
    {
        try
        {
            _key = string.IsNullOrWhiteSpace(base64Key) ? [] : Convert.FromBase64String(base64Key);
        }
        catch (FormatException)
        {
            _key = [];
        }
    }

    public bool IsConfigured => _key.Length == 32;

    public static bool TryNormalize(string? value, out string iban)
    {
        iban = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var normalized = value.Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        if (!IranianIbanPattern.IsMatch(normalized))
            return false;

        var rearranged = normalized[4..] + normalized[..4];
        var remainder = 0;
        foreach (var character in rearranged)
        {
            if (character is >= '0' and <= '9')
                remainder = (remainder * 10 + character - '0') % 97;
            else
            {
                var numeric = character - 'A' + 10;
                remainder = (remainder * 100 + numeric) % 97;
            }
        }
        if (remainder != 1)
            return false;
        iban = normalized;
        return true;
    }

    public string Protect(string courierId, string iban)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Courier payout IBAN encryption is not configured.");
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(iban);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, Encoding.UTF8.GetBytes(courierId));
        CryptographicOperations.ZeroMemory(plaintext);
        return $"v1:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(ciphertext)}";
    }

    public static string Fingerprint(string courierId, string iban)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(courierId + "\0" + iban));
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    public static string Mask(string iban) => "IR" + new string('*', 20) + iban[^4..];
}
