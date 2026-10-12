using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Mono.Api.Security;
using Xunit;

namespace Mono.Api.Tests;

public sealed class CustomerContextTests
{
    [Fact]
    public void Customer_context_requires_an_authenticated_identity()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(MonoClaimTypes.CustomerId, "cus_0123456789abcdef0123456789abcdef")]));
        var accessor = CreateAccessor(principal);

        Assert.Throws<CustomerContextUnavailableException>(() => new HttpCustomerContext(accessor).CustomerId);
    }

    [Fact]
    public void Customer_context_rejects_a_missing_customer_claim()
    {
        var accessor = CreateAccessor(AuthenticatedIdentity());

        Assert.Throws<CustomerContextUnavailableException>(() => new HttpCustomerContext(accessor).CustomerId);
    }

    [Theory]
    [InlineData("customer-123")]
    [InlineData("")]
    public void Customer_context_rejects_malformed_customer_ids(string customerId)
    {
        var accessor = CreateAccessor(AuthenticatedIdentity(
            new Claim(MonoClaimTypes.CustomerId, customerId)));

        Assert.Throws<CustomerContextUnavailableException>(() => new HttpCustomerContext(accessor).CustomerId);
    }

    [Fact]
    public void Customer_context_rejects_ambiguous_customer_claims()
    {
        var accessor = CreateAccessor(AuthenticatedIdentity(
            new Claim(MonoClaimTypes.CustomerId, "cus_0123456789abcdef0123456789abcdef"),
            new Claim(MonoClaimTypes.CustomerId, "cus_fedcba9876543210fedcba9876543210")));

        Assert.Throws<CustomerContextUnavailableException>(() => new HttpCustomerContext(accessor).CustomerId);
    }

    [Fact]
    public void Customer_context_reads_only_the_authenticated_principal_claim()
    {
        var expected = "cus_0123456789abcdef0123456789abcdef";
        var accessor = CreateAccessor(AuthenticatedIdentity(new Claim(MonoClaimTypes.CustomerId, expected)));

        Assert.Equal(expected, new HttpCustomerContext(accessor).CustomerId);
    }

    [Fact]
    public async Task Customer_tenant_authorization_fails_closed_without_valid_context()
    {
        var accessor = CreateAccessor(new ClaimsPrincipal(new ClaimsIdentity()));
        var requirement = new CustomerTenantRequirement();
        var authorizationContext = new AuthorizationHandlerContext(
            [requirement],
            new ClaimsPrincipal(new ClaimsIdentity()),
            resource: null);
        var handler = new CustomerTenantAuthorizationHandler(new HttpCustomerContext(accessor));

        await handler.HandleAsync(authorizationContext);

        Assert.False(authorizationContext.HasSucceeded);
    }

    [Fact]
    public async Task Customer_tenant_authorization_succeeds_for_valid_authenticated_context()
    {
        var customerId = "cus_0123456789abcdef0123456789abcdef";
        var principal = new ClaimsPrincipal(AuthenticatedIdentity(
            new Claim(MonoClaimTypes.CustomerId, customerId)));
        var accessor = CreateAccessor(principal);
        var requirement = new CustomerTenantRequirement();
        var authorizationContext = new AuthorizationHandlerContext([requirement], principal, resource: null);
        var handler = new CustomerTenantAuthorizationHandler(new HttpCustomerContext(accessor));

        await handler.HandleAsync(authorizationContext);

        Assert.True(authorizationContext.HasSucceeded);
    }

    private static HttpContextAccessor CreateAccessor(ClaimsIdentity identity) =>
        CreateAccessor(new ClaimsPrincipal(identity));

    private static HttpContextAccessor CreateAccessor(ClaimsPrincipal principal) =>
        new() { HttpContext = new DefaultHttpContext { User = principal } };

    private static ClaimsIdentity AuthenticatedIdentity(params Claim[] claims) =>
        new(claims, authenticationType: "test-authentication");
}
