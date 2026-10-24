using System.Collections.Frozen;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Mono.Api.Domain;

public static class WebhookEventCatalog
{
    private static readonly FrozenSet<string> Supported = new[]
    {
        "mission.accepted", "mission.rejected", "promise.committed", "courier.assigned",
        "courier.arrived_pickup", "mission.handed_to_courier", "mission.picked_up",
        "mission.in_transit", "mission.sla_at_risk", "mission.delivered", "mission.failed",
        "mission.cancelled", "mission.return_required", "mission.return_completed"
    }.ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> All => Supported;
}

public static class WebhookEndpointValidator
{
    public static IReadOnlyList<string> Validate(string? url, IReadOnlyCollection<string>? events)
    {
        var errors = new List<string>();
        if (!Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 || string.IsNullOrWhiteSpace(uri.Host) || uri.UserInfo.Length > 0 ||
            uri.Fragment.Length > 0 || uri.Query.Length > 0 || uri.AbsoluteUri.Length > 2048)
        {
            errors.Add("WEBHOOK_URL_INVALID");
        }
        else
        {
            var host = uri.DnsSafeHost.TrimEnd('.');
            if (IPAddress.TryParse(host, out _) || IsLocalHost(host) || !IsValidDnsName(host))
                errors.Add("WEBHOOK_URL_HOST_INVALID");
        }

        if (events is null || events.Count is < 1 or > 14 ||
            events.Any(e => string.IsNullOrWhiteSpace(e) || !WebhookEventCatalog.All.Contains(e)) ||
            events.Distinct(StringComparer.Ordinal).Count() != events.Count)
        {
            errors.Add("WEBHOOK_EVENTS_INVALID");
        }

        return errors;
    }

    private static bool IsLocalHost(string host) =>
        host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".test", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".example", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidDnsName(string host)
    {
        try
        {
            var ascii = new IdnMapping().GetAscii(host);
            return ascii.Length <= 253 && ascii.Contains('.') &&
                   ascii.Split('.').All(label => label.Length is > 0 and <= 63 &&
                       label[0] != '-' && label[^1] != '-' &&
                       label.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-'));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public sealed class WebhookSecretProtector
{
    private readonly byte[] _key;

    public WebhookSecretProtector(string? base64Key)
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

    public string Protect(string secret)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Webhook secret protection is not configured.");

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(secret);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, tag.Length);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        CryptographicOperations.ZeroMemory(plaintext);
        return $"v1:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(tag)}:{Convert.ToBase64String(ciphertext)}";
    }

    public string Unprotect(string protectedValue)
    {
        var plaintext = UnprotectBytes(protectedValue);
        try
        {
            return new UTF8Encoding(false, true).GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public byte[] UnprotectBytes(string protectedValue)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Webhook secret protection is not configured.");

        var parts = protectedValue.Split(':');
        if (parts.Length != 4 || parts[0] != "v1")
            throw new CryptographicException("The protected webhook secret format is invalid.");
        var nonce = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var ciphertext = Convert.FromBase64String(parts[3]);
        if (nonce.Length != 12 || tag.Length != 16)
            throw new CryptographicException("The protected webhook secret dimensions are invalid.");
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_key, tag.Length);
        try
        {
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
    }
}
