using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Mono.Api.Domain;

public static class WebhookOperationsAuditCursor
{
    public static string Encode(DateTimeOffset occurredAt, string auditId, string? actorId, string? action)
    {
        var value = string.Join('|', occurredAt.ToString("O", CultureInfo.InvariantCulture), auditId,
            FilterFingerprint(actorId, action));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    public static bool TryDecode(
        string? cursor,
        string? actorId,
        string? action,
        out DateTimeOffset occurredAt,
        out string auditId)
    {
        occurredAt = default;
        auditId = string.Empty;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor ?? string.Empty)).Split('|');
            if (parts.Length != 3 || parts[1].Length != 36 ||
                !parts[1].StartsWith("woa_", StringComparison.Ordinal) ||
                parts[1][4..].Any(ch => !char.IsAsciiHexDigit(ch) || char.IsUpper(ch)) ||
                !DateTimeOffset.TryParseExact(parts[0], "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsedAt) ||
                !string.Equals(parts[2], FilterFingerprint(actorId, action), StringComparison.Ordinal))
                return false;
            occurredAt = parsedAt;
            auditId = parts[1];
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string FilterFingerprint(string? actorId, string? action)
    {
        var input = string.Join('\n', actorId ?? string.Empty, action ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant()[..16];
    }
}
