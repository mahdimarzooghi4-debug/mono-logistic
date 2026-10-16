using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Mono.Api.Tests;

public sealed class MonoRoutePlanOperationsEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Route_plan_initialization_requires_mfa_operations_administrator()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsync(
            "/operations/v1/routes/route-1/plan",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/operations/v1/routes/route-1/plan")]
    [InlineData("/operations/v1/routes/route-1/plan/versions/1")]
    public async Task Route_plan_reads_require_mfa_operations_administrator(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
