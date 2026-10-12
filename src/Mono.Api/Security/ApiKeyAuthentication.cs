using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Mono.Api.Persistence;
using Npgsql;

namespace Mono.Api.Security;

public static class MonoApiKeyDefaults
{
    public const string Scheme = "MonoApiKey";
    public const string HeaderName = "X-Mono-API-Key";
}

public sealed record ApiKeyCredential(string KeyId, string CustomerId, string SecretHash,
    DateTimeOffset ExpiresAt, DateTimeOffset? RevokedAt, bool CustomerActive, IReadOnlyList<string> Scopes);

public interface IApiKeyCredentialStore
{
    Task<ApiKeyCredential?> FindAsync(string keyId, CancellationToken cancellationToken);
}

public static class ApiKeyCredentialVerifier
{
    private static readonly Regex KeyPattern = new(@"\Amono_live_([0-9a-f]{16})_([0-9a-f]{64})\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CustomerPattern = new(@"\Acus_[0-9a-f]{32}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static string? GetKeyId(string key)
    {
        var match = KeyPattern.Match(key);
        return match.Success ? "key_" + match.Groups[1].Value : null;
    }

    public static string ComputeHash(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    public static bool TryValidate(string presentedKey, ApiKeyCredential? credential,
        DateTimeOffset now, out ClaimsPrincipal? principal)
    {
        principal = null;
        var keyId = GetKeyId(presentedKey);
        if (keyId is null || credential is null ||
            !string.Equals(keyId, credential.KeyId, StringComparison.Ordinal) ||
            !CustomerPattern.IsMatch(credential.CustomerId) || !credential.CustomerActive ||
            credential.RevokedAt is not null || credential.ExpiresAt <= now ||
            !credential.Scopes.Any(scope => !string.IsNullOrWhiteSpace(scope)))
            return false;

        byte[] providedHash;
        byte[] storedHash;
        try
        {
            providedHash = Convert.FromHexString(ComputeHash(presentedKey));
            storedHash = Convert.FromHexString(credential.SecretHash);
        }
        catch (FormatException)
        {
            return false;
        }

        if (storedHash.Length != providedHash.Length ||
            !CryptographicOperations.FixedTimeEquals(providedHash, storedHash))
            return false;

        var claims = new List<Claim>
        {
            new(MonoClaimTypes.CustomerId, credential.CustomerId),
            new("mono/api_key_id", credential.KeyId),
            new(ClaimTypes.NameIdentifier, credential.KeyId)
        };
        claims.AddRange(credential.Scopes.Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => new Claim("mono/scope", scope)));
        principal = new ClaimsPrincipal(new ClaimsIdentity(claims, MonoApiKeyDefaults.Scheme));
        return true;
    }
}

public sealed class MonoApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
}

public sealed class MonoApiKeyAuthenticationHandler(
    IOptionsMonitor<MonoApiKeyAuthenticationOptions> options,
    ILoggerFactory logger,
    System.Text.Encodings.Web.UrlEncoder encoder,
    IServiceProvider services,
    TimeProvider timeProvider)
    : AuthenticationHandler<MonoApiKeyAuthenticationOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(MonoApiKeyDefaults.HeaderName, out var values))
            return AuthenticateResult.NoResult();
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            return AuthenticateResult.Fail("Invalid API key.");

        var key = values[0]!;
        var keyId = ApiKeyCredentialVerifier.GetKeyId(key);
        if (keyId is null)
            return AuthenticateResult.Fail("Invalid API key.");

        var credentialStore = services.GetRequiredService<IApiKeyCredentialStore>();
        var credential = await credentialStore.FindAsync(keyId, Context.RequestAborted);
        return ApiKeyCredentialVerifier.TryValidate(key, credential, timeProvider.GetUtcNow(), out var principal)
            ? AuthenticateResult.Success(new AuthenticationTicket(principal!, Scheme.Name))
            : AuthenticateResult.Fail("Invalid API key.");
    }
}

public static class MonoApiKeyServiceCollectionExtensions
{
    public static IServiceCollection AddMonoApiKeyAuthentication(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<NpgsqlDataSource>(_ =>
        {
            var connectionString = configuration.GetConnectionString("Mono");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("ConnectionStrings:Mono is required for API key authentication.");
            return NpgsqlDataSource.Create(connectionString);
        });
        services.AddScoped<IApiKeyCredentialStore, PostgresApiKeyCredentialStore>();
        services.AddAuthentication()
            .AddScheme<MonoApiKeyAuthenticationOptions, MonoApiKeyAuthenticationHandler>(
                MonoApiKeyDefaults.Scheme, _ => { });
        return services;
    }
}
