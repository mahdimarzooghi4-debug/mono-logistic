using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Mono.Api.Domain;

public static class WebhookDeliveryHistoryCursor
{
    public static string Encode(
        DateTimeOffset createdAt,
        string deliveryId,
        string? endpointId,
        string? eventType,
        string? status,
        string? customerId = null)
    {
        var value = string.Join('|', createdAt.ToString("O", CultureInfo.InvariantCulture),
            deliveryId, FilterFingerprint(endpointId, eventType, status, customerId));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }

    public static bool TryDecode(
        string? cursor,
        string? endpointId,
        string? eventType,
        string? status,
        out DateTimeOffset createdAt,
        out string deliveryId,
        string? customerId = null)
    {
        createdAt = default;
        deliveryId = string.Empty;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor ?? string.Empty)).Split('|');
            if (parts.Length != 3 || parts[1].Length != 36 ||
                !parts[1].StartsWith("whd_", StringComparison.Ordinal) ||
                parts[1][4..].Any(ch => !char.IsAsciiHexDigit(ch) || char.IsUpper(ch)) ||
                !DateTimeOffset.TryParseExact(parts[0], "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsedAt) ||
                !string.Equals(parts[2], FilterFingerprint(endpointId, eventType, status, customerId), StringComparison.Ordinal))
                return false;
            createdAt = parsedAt;
            deliveryId = parts[1];
            return true;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static string FilterFingerprint(string? endpointId, string? eventType, string? status, string? customerId)
    {
        var input = customerId is null
            ? string.Join('\n', endpointId ?? string.Empty, eventType ?? string.Empty, status ?? string.Empty)
            : string.Join('\n', endpointId ?? string.Empty, eventType ?? string.Empty,
                status ?? string.Empty, customerId);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant()[..16];
    }
}
