using System.Net;
using Mono.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Mono.Api.Tests;

public sealed class BranchEndpointAuthorizationTests
{
    private const string ApiKey =
        "mono_live_0123456789abcdef_0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Branch_list_requires_authentication()
    {
        await using var factory = CreateFactory(["branches.read"]);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/v1/branches");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Branch_list_requires_the_branches_read_scope()
    {
        await using var factory = CreateFactory(["missions.read"]);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(MonoApiKeyDefaults.HeaderName, ApiKey);

        var response = await client.GetAsync("/v1/branches");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(IReadOnlyList<string> scopes) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IApiKeyCredentialStore>();
                services.AddSingleton<IApiKeyCredentialStore>(new FixedCredentialStore(
                    new ApiKeyCredential(
                        "key_0123456789abcdef",
                        "cus_0123456789abcdef0123456789abcdef",
                        ApiKeyCredentialVerifier.ComputeHash(ApiKey),
                        DateTimeOffset.UtcNow.AddHours(1),
                        null,
                        true,
                        scopes)));
            }));

    private sealed class FixedCredentialStore(ApiKeyCredential credential) : IApiKeyCredentialStore
    {
        public Task<ApiKeyCredential?> FindAsync(string keyId, CancellationToken cancellationToken) =>
            Task.FromResult<ApiKeyCredential?>(keyId == credential.KeyId ? credential : null);
    }
}
