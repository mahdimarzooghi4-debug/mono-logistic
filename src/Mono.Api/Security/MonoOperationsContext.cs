using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Mono.Api.Security;

/// <summary>
/// Identity supplied by a trusted Mono staff authentication boundary.
/// Implementations must derive actor and roles from authenticated server-side claims,
/// never from request data or configuration authoring arguments.
/// </summary>
public interface IMonoOperationsContext
{
    string ActorId { get; }
    IReadOnlyCollection<string> Roles { get; }
}

public static class MonoOperationsRoles
{
    public const string Administrator = "MONO_ADMIN";
}

public static class MonoOperationsClaimTypes
{
    public const string ActorId = "sub";
    public const string Role = "mono/role";
    public const string MultiFactor = "mono/mfa";
    internal const string IdentityDomain = "mono/identity_domain";
}

public static class MonoOperationsDefaults
{
    public const string Scheme = "MonoOperationsJwt";
    public const string IdentityDomainValue = "operations";
}

public sealed class MonoOperationsContextUnavailableException(string message)
    : UnauthorizedAccessException(message)
{
}

public sealed class HttpMonoOperationsContext(IHttpContextAccessor httpContextAccessor)
    : IMonoOperationsContext
{
    public string ActorId
    {
        get
        {
            var subjects = GetTrustedIdentity().FindAll(MonoOperationsClaimTypes.ActorId).ToArray();
            if (subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0].Value))
                throw new MonoOperationsContextUnavailableException(
                    "The authenticated operations identity must contain exactly one subject.");
            return subjects[0].Value;
        }
    }

    public IReadOnlyCollection<string> Roles
    {
        get
        {
            var identity = GetTrustedIdentity();
            var roles = identity.FindAll(MonoOperationsClaimTypes.Role)
                .Select(claim => claim.Value)
                .Where(role => !string.IsNullOrWhiteSpace(role))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var multiFactorClaims = identity.FindAll(MonoOperationsClaimTypes.MultiFactor).ToArray();
            var hasVerifiedMfa = multiFactorClaims.Length == 1 &&
                string.Equals(multiFactorClaims[0].Value, "true", StringComparison.Ordinal);
            return hasVerifiedMfa
                ? roles
                : roles.Where(role => !string.Equals(
                    role, MonoOperationsRoles.Administrator, StringComparison.Ordinal)).ToArray();
        }
    }

    private ClaimsIdentity GetTrustedIdentity()
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var identities = principal?.Identities
            .Where(identity => identity.IsAuthenticated &&
                identity.FindAll(MonoOperationsClaimTypes.IdentityDomain)
                    .Count(claim => string.Equals(
                        claim.Value, MonoOperationsDefaults.IdentityDomainValue, StringComparison.Ordinal)) == 1)
            .ToArray() ?? [];
        if (identities.Length != 1)
            throw new MonoOperationsContextUnavailableException(
                "A validated Mono operations identity is required.");
        return identities[0];
    }
}
