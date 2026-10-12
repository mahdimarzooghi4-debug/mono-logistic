using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Mono.Api.Security;

public sealed record IssuedApiKey(string Value, ApiKeyCredential Credential);

public static class MonoApiKeyScopeCatalog
{
    private static readonly FrozenSet<string> SupportedScopes = new[]
    {
        "branches.read",
        "delivery-requests.write",
        "delivery-requests.read",
        "missions.cancel"
    }.ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlySet<string> All => SupportedScopes;

    public static bool IsSupported(string scope) => SupportedScopes.Contains(scope);
}

public static class MonoApiKeyIssuer
{
    private static readonly Regex CustomerIdPattern = new(
        @"\Acus_[0-9a-f]{32}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IssuedApiKey Create(
        string customerId,
        IEnumerable<string> scopes,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(scopes);

        var normalizedCustomerId = customerId.Trim();
        if (!CustomerIdPattern.IsMatch(normalizedCustomerId))
            throw new ArgumentException("A valid Mono customer ID is required.", nameof(customerId));
        if (expiresAt <= now)
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "The API key expiry must be in the future.");

        var requestedScopes = scopes
            .Select(scope => scope?.Trim() ?? string.Empty)
            .ToArray();
        if (requestedScopes.Length == 0 || requestedScopes.Any(scope => scope.Length == 0))
            throw new ArgumentException("At least one non-empty supported scope is required.", nameof(scopes));

        var scopeList = requestedScopes
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (scopeList.Any(scope => !MonoApiKeyScopeCatalog.IsSupported(scope)))
            throw new ArgumentException("One or more API key scopes are not supported.", nameof(scopes));

        var keyIdSuffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var rawKey = $"mono_live_{keyIdSuffix}_{secret}";
        var credential = new ApiKeyCredential(
            "key_" + keyIdSuffix,
            normalizedCustomerId,
            ApiKeyCredentialVerifier.ComputeHash(rawKey),
            expiresAt,
            null,
            true,
            scopeList);

        return new IssuedApiKey(rawKey, credential);
    }
}
