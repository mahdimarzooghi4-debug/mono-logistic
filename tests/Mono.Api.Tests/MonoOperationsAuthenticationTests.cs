using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Mono.Api.Security;
using Xunit;

namespace Mono.Api.Tests;

public sealed class MonoOperationsAuthenticationTests
{
    [Fact]
    public void Trusted_admin_identity_requires_exactly_one_subject_and_verified_mfa()
    {
        var accessor = Accessor(new ClaimsIdentity(
        [
            new Claim(MonoOperationsClaimTypes.ActorId, "staff-123"),
            new Claim(MonoOperationsClaimTypes.Role, MonoOperationsRoles.Administrator),
            new Claim(MonoOperationsClaimTypes.MultiFactor, "true"),
            new Claim(MonoOperationsClaimTypes.IdentityDomain, MonoOperationsDefaults.IdentityDomainValue)
        ], MonoOperationsDefaults.Scheme));
        var context = new HttpMonoOperationsContext(accessor);

        Assert.Equal("staff-123", context.ActorId);
        Assert.Contains(MonoOperationsRoles.Administrator, context.Roles);
    }

    [Fact]
    public void Admin_role_without_mfa_is_not_authorized()
    {
        var accessor = Accessor(new ClaimsIdentity(
        [
            new Claim(MonoOperationsClaimTypes.ActorId, "staff-123"),
            new Claim(MonoOperationsClaimTypes.Role, MonoOperationsRoles.Administrator),
            new Claim(MonoOperationsClaimTypes.IdentityDomain, MonoOperationsDefaults.IdentityDomainValue)
        ], MonoOperationsDefaults.Scheme));
        var context = new HttpMonoOperationsContext(accessor);

        Assert.DoesNotContain(MonoOperationsRoles.Administrator, context.Roles);
    }

    [Fact]
    public void Customer_api_key_identity_cannot_be_used_as_an_operations_identity()
    {
        var accessor = Accessor(new ClaimsIdentity(
        [
            new Claim(MonoOperationsClaimTypes.ActorId, "key_123"),
            new Claim(MonoOperationsClaimTypes.Role, MonoOperationsRoles.Administrator),
            new Claim(MonoOperationsClaimTypes.MultiFactor, "true")
        ], MonoApiKeyDefaults.Scheme));
        var context = new HttpMonoOperationsContext(accessor);

        Assert.Throws<MonoOperationsContextUnavailableException>(() => _ = context.ActorId);
    }

    [Fact]
    public void Duplicate_subjects_fail_closed()
    {
        var accessor = Accessor(new ClaimsIdentity(
        [
            new Claim(MonoOperationsClaimTypes.ActorId, "staff-123"),
            new Claim(MonoOperationsClaimTypes.ActorId, "staff-456"),
            new Claim(MonoOperationsClaimTypes.Role, MonoOperationsRoles.Administrator),
            new Claim(MonoOperationsClaimTypes.MultiFactor, "true"),
            new Claim(MonoOperationsClaimTypes.IdentityDomain, MonoOperationsDefaults.IdentityDomainValue)
        ], MonoOperationsDefaults.Scheme));
        var context = new HttpMonoOperationsContext(accessor);

        Assert.Throws<MonoOperationsContextUnavailableException>(() => _ = context.ActorId);
    }

    private static IHttpContextAccessor Accessor(ClaimsIdentity identity)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        accessor.HttpContext.User = new ClaimsPrincipal(identity);
        return accessor;
    }
}
