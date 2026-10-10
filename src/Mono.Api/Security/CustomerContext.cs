using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Mono.Api.Security;

public static class MonoClaimTypes
{
    public const string CustomerId = "mono/customer_id";
    public const string Scope = "mono/scope";
}

public static class MonoAuthorizationPolicies
{
    public const string CustomerTenant = "mono.customer-tenant";
    public const string BranchesRead = "mono.branches.read";
    public const string DeliveryRequestsWrite = "mono.delivery-requests.write";
}

public interface ICustomerContext
{
    string CustomerId { get; }
}

public sealed class CustomerContextUnavailableException(string message) : UnauthorizedAccessException(message)
{
}

public sealed class HttpCustomerContext(IHttpContextAccessor httpContextAccessor) : ICustomerContext
{
    private static readonly Regex CustomerIdPattern = new(
        @"\Acus_[0-9a-f]{32}\z",
        RegexOptions.CultureInvariant);

    public string CustomerId
    {
        get
        {
            var principal = httpContextAccessor.HttpContext?.User;
            if (principal?.Identity?.IsAuthenticated != true)
            {
                throw new CustomerContextUnavailableException("An authenticated customer identity is required.");
            }

            var customerClaims = principal.FindAll(MonoClaimTypes.CustomerId).ToArray();
            if (customerClaims.Length != 1 || !CustomerIdPattern.IsMatch(customerClaims[0].Value))
            {
                throw new CustomerContextUnavailableException("The authenticated identity must contain exactly one valid Mono customer ID.");
            }

            return customerClaims[0].Value;
        }
    }
}

public sealed class CustomerTenantRequirement : IAuthorizationRequirement
{
}

public sealed class CustomerTenantAuthorizationHandler(ICustomerContext customerContext)
    : AuthorizationHandler<CustomerTenantRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CustomerTenantRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            try
            {
                _ = customerContext.CustomerId;
                context.Succeed(requirement);
            }
            catch (CustomerContextUnavailableException)
            {
                // A missing, malformed, or ambiguous tenant identity fails closed.
            }
        }

        return Task.CompletedTask;
    }
}

public sealed record MonoScopeRequirement(string RequiredScope) : IAuthorizationRequirement;

public sealed class MonoScopeAuthorizationHandler : AuthorizationHandler<MonoScopeRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MonoScopeRequirement requirement)
    {
        if (context.User.FindAll(MonoClaimTypes.Scope)
            .Any(claim => string.Equals(claim.Value, requirement.RequiredScope, StringComparison.Ordinal)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class CustomerTenantServiceCollectionExtensions
{
    public static IServiceCollection AddMonoCustomerTenantAuthorization(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<ICustomerContext, HttpCustomerContext>();
        services.AddScoped<IAuthorizationHandler, CustomerTenantAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, MonoScopeAuthorizationHandler>();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(MonoAuthorizationPolicies.CustomerTenant, policy =>
            {
                policy.AddAuthenticationSchemes(MonoApiKeyDefaults.Scheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new CustomerTenantRequirement());
            });
            options.AddPolicy(MonoAuthorizationPolicies.BranchesRead, policy =>
            {
                policy.AddAuthenticationSchemes(MonoApiKeyDefaults.Scheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new CustomerTenantRequirement());
                policy.AddRequirements(new MonoScopeRequirement("branches.read"));
            });
            options.AddPolicy(MonoAuthorizationPolicies.DeliveryRequestsWrite, policy =>
            {
                policy.AddAuthenticationSchemes(MonoApiKeyDefaults.Scheme);
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new CustomerTenantRequirement());
                policy.AddRequirements(new MonoScopeRequirement("delivery-requests.write"));
            });
        });

        return services;
    }
}
