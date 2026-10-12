using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace Mono.Api.Security;

public static class MonoOperationsAuthorizationPolicies
{
    public const string Administrator = "mono.operations.admin";
}

public static class MonoOperationsAuthenticationExtensions
{
    public static IServiceCollection AddMonoOperationsAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authority = configuration["MonoOperations:Authority"];
        var audience = configuration["MonoOperations:Audience"];
        var hasAuthority = !string.IsNullOrWhiteSpace(authority);
        var hasAudience = !string.IsNullOrWhiteSpace(audience);
        if (hasAuthority != hasAudience)
            throw new InvalidOperationException(
                "MonoOperations:Authority and MonoOperations:Audience must be configured together.");

        if (hasAuthority &&
            (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) ||
             !string.Equals(authorityUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("MonoOperations:Authority must be an HTTPS URL.");

        services.AddHttpContextAccessor();
        services.AddScoped<IMonoOperationsContext, HttpMonoOperationsContext>();
        services.AddAuthentication()
            .AddJwtBearer(MonoOperationsDefaults.Scheme, options =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = true;
                if (hasAuthority)
                {
                    options.Authority = authority;
                    options.Audience = audience;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidAudience = audience,
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime = true,
                        NameClaimType = MonoOperationsClaimTypes.ActorId,
                        RoleClaimType = MonoOperationsClaimTypes.Role
                    };
                }

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (!hasAuthority || !hasAudience)
                            context.Fail("Mono operations token validation is not configured.");
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var identity = context.Principal?.Identities
                            .SingleOrDefault(candidate => candidate.IsAuthenticated);
                        var subjects = identity?.FindAll(MonoOperationsClaimTypes.ActorId).ToArray() ?? [];
                        var domainClaims = identity?.FindAll(MonoOperationsClaimTypes.IdentityDomain).ToArray() ?? [];
                        if (identity is null || subjects.Length != 1 ||
                            string.IsNullOrWhiteSpace(subjects[0].Value) || domainClaims.Length != 0)
                        {
                            context.Fail("The operations token has an invalid subject or reserved claim.");
                            return Task.CompletedTask;
                        }

                        var roles = identity.FindAll(MonoOperationsClaimTypes.Role).ToArray();
                        var mfaClaims = identity.FindAll(MonoOperationsClaimTypes.MultiFactor).ToArray();
                        var hasAdminRole = roles.Any(claim => string.Equals(
                            claim.Value, MonoOperationsRoles.Administrator, StringComparison.Ordinal));
                        if (hasAdminRole && (mfaClaims.Length != 1 ||
                            !string.Equals(mfaClaims[0].Value, "true", StringComparison.Ordinal)))
                        {
                            context.Fail("Mono administrator access requires verified MFA.");
                            return Task.CompletedTask;
                        }

                        identity.AddClaim(new System.Security.Claims.Claim(
                            MonoOperationsClaimTypes.IdentityDomain,
                            MonoOperationsDefaults.IdentityDomainValue,
                            System.Security.Claims.ClaimValueTypes.String,
                            MonoOperationsDefaults.Scheme));
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(MonoOperationsAuthorizationPolicies.Administrator, policy =>
            {
                policy.AddAuthenticationSchemes(MonoOperationsDefaults.Scheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(MonoOperationsClaimTypes.Role, MonoOperationsRoles.Administrator);
                policy.RequireClaim(MonoOperationsClaimTypes.MultiFactor, "true");
            });
        });
        return services;
    }
}
