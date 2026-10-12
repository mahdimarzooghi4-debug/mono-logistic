using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using NpgsqlTypes;

namespace Mono.Api.Security;

public static class MonoCourierAuthorizationPolicies
{
    public const string Courier = "mono.courier.user";
}

public static class MonoCourierClaimTypes
{
    public const string Subject = "sub";
    public const string Role = "mono/role";
    public const string CourierId = "mono/courier_id";
    public const string IdentityDomain = "mono/identity_domain";
}

public static class MonoCourierDefaults
{
    public const string Scheme = "MonoCourierJwt";
    public const string IdentityDomainValue = "courier";
    public const string RoleValue = "COURIER";
}

public interface IMonoCourierContext
{
    string CourierId { get; }
}

public sealed class MonoCourierContextUnavailableException(string message)
    : UnauthorizedAccessException(message)
{
}

public sealed class HttpMonoCourierContext(IHttpContextAccessor httpContextAccessor)
    : IMonoCourierContext
{
    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public string CourierId
    {
        get
        {
            var identities = httpContextAccessor.HttpContext?.User.Identities
                .Where(identity => identity.IsAuthenticated &&
                    identity.FindAll(MonoCourierClaimTypes.IdentityDomain)
                        .Count(claim => string.Equals(
                            claim.Value, MonoCourierDefaults.IdentityDomainValue, StringComparison.Ordinal)) == 1)
                .ToArray() ?? [];
            if (identities.Length != 1)
                throw new MonoCourierContextUnavailableException(
                    "A validated Mono courier identity is required.");

            var courierIds = identities[0].FindAll(MonoCourierClaimTypes.CourierId).ToArray();
            if (courierIds.Length != 1 || !CourierIdPattern.IsMatch(courierIds[0].Value))
                throw new MonoCourierContextUnavailableException(
                    "The authenticated courier identity has an invalid Courier ID.");
            return courierIds[0].Value;
        }
    }
}

/// <summary>
/// Validates courier JWT identity claims against the current persisted courier state.
/// The configured authority is trusted to issue mono/courier_id only after verified account binding.
/// </summary>
public sealed class MonoCourierTokenValidator(NpgsqlDataSource dataSource)
{
    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<bool> ValidateAsync(
        ClaimsPrincipal? principal,
        CancellationToken cancellationToken = default)
    {
        var identities = principal?.Identities.Where(identity => identity.IsAuthenticated).ToArray() ?? [];
        if (identities.Length != 1)
            return false;

        var identity = identities[0];
        var subjects = identity.FindAll(MonoCourierClaimTypes.Subject).ToArray();
        var courierIds = identity.FindAll(MonoCourierClaimTypes.CourierId).ToArray();
        var roles = identity.FindAll(MonoCourierClaimTypes.Role).ToArray();
        var domainClaims = identity.FindAll(MonoCourierClaimTypes.IdentityDomain).ToArray();
        if (subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0].Value) ||
            courierIds.Length != 1 || !CourierIdPattern.IsMatch(courierIds[0].Value) ||
            roles.Length != 1 || !string.Equals(roles[0].Value, MonoCourierDefaults.RoleValue, StringComparison.Ordinal) ||
            domainClaims.Length != 0)
            return false;

        const string sql = """
            SELECT courier.status,
                COALESCE(latest.action = 'SUSPENDED', FALSE)
            FROM couriers courier
            LEFT JOIN LATERAL (
                SELECT action FROM courier_operational_status_events
                WHERE courier_id = courier.courier_id
                ORDER BY event_sequence DESC LIMIT 1
            ) latest ON TRUE
            WHERE courier.courier_id = @courier_id;
            """;
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("courier_id", NpgsqlDbType.Text, courierIds[0].Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) ||
            !string.Equals(reader.GetString(0), "ACTIVE", StringComparison.Ordinal) ||
            reader.GetBoolean(1))
            return false;

        identity.AddClaim(new Claim(
            MonoCourierClaimTypes.IdentityDomain,
            MonoCourierDefaults.IdentityDomainValue,
            ClaimValueTypes.String,
            MonoCourierDefaults.Scheme));
        return true;
    }
}

public static class MonoCourierAuthenticationExtensions
{
    public static IServiceCollection AddMonoCourierAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authority = configuration["MonoCourier:Authority"];
        var audience = configuration["MonoCourier:Audience"];
        var hasAuthority = !string.IsNullOrWhiteSpace(authority);
        var hasAudience = !string.IsNullOrWhiteSpace(audience);
        if (hasAuthority != hasAudience)
            throw new InvalidOperationException(
                "MonoCourier:Authority and MonoCourier:Audience must be configured together.");

        if (hasAuthority &&
            (!Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri) ||
             !string.Equals(authorityUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("MonoCourier:Authority must be an HTTPS URL.");

        services.AddHttpContextAccessor();
        services.AddScoped<IMonoCourierContext, HttpMonoCourierContext>();
        services.AddScoped<MonoCourierTokenValidator>();
        services.AddAuthentication()
            .AddJwtBearer(MonoCourierDefaults.Scheme, options =>
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
                        NameClaimType = MonoCourierClaimTypes.Subject,
                        RoleClaimType = MonoCourierClaimTypes.Role
                    };
                }

                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (!hasAuthority || !hasAudience)
                            context.Fail("Mono courier token validation is not configured.");
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = async context =>
                    {
                        var validator = context.HttpContext.RequestServices
                            .GetRequiredService<MonoCourierTokenValidator>();
                        if (!await validator.ValidateAsync(
                                context.Principal, context.HttpContext.RequestAborted))
                            context.Fail("The courier token is not bound to an active, unsuspended Mono courier.");
                    }
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(MonoCourierAuthorizationPolicies.Courier, policy =>
            {
                policy.AddAuthenticationSchemes(MonoCourierDefaults.Scheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(MonoCourierClaimTypes.IdentityDomain, MonoCourierDefaults.IdentityDomainValue);
                policy.RequireClaim(MonoCourierClaimTypes.Role, MonoCourierDefaults.RoleValue);
            });
        });
        return services;
    }
}
