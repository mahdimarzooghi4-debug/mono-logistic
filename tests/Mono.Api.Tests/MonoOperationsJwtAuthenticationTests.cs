using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Mono.Api.Security;
using Xunit;

namespace Mono.Api.Tests;

public sealed class MonoOperationsJwtAuthenticationTests
{
    private const string Authority = "https://identity.mono-test.invalid";
    private const string Audience = "mono-api-test";

    [Fact]
    public async Task Jwt_validation_enforces_signature_issuer_audience_expiration_and_mfa()
    {
        using var trustedRsa = RSA.Create(2048);
        using var otherRsa = RSA.Create(2048);
        var trustedKey = new RsaSecurityKey(trustedRsa) { KeyId = "trusted-test-key" };
        await using var app = await CreateAppAsync(Authority, Audience, trustedKey);
        using var client = app.GetTestClient();

        var valid = await client.GetAsync("/operations");
        Assert.Equal(System.Net.HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal("staff-1", await valid.Content.ReadAsStringAsync());

        var missingMfa = CreateToken(trustedKey, Authority, Audience, includeMfa: false);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
            (await SendTokenAsync(client, missingMfa)).StatusCode);

        var wrongIssuer = CreateToken(trustedKey, "https://wrong-issuer.invalid", Audience);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
            (await SendTokenAsync(client, wrongIssuer)).StatusCode);

        var wrongAudience = CreateToken(trustedKey, Authority, "wrong-audience");
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
            (await SendTokenAsync(client, wrongAudience)).StatusCode);

        var invalidSignature = CreateToken(
            new RsaSecurityKey(otherRsa) { KeyId = "other-test-key" }, Authority, Audience);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
            (await SendTokenAsync(client, invalidSignature)).StatusCode);

        var expired = CreateToken(trustedKey, Authority, Audience, expiresInMinutes: -15);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
            (await SendTokenAsync(client, expired)).StatusCode);
    }

    [Fact]
    public async Task Operations_tokens_are_rejected_when_issuer_and_audience_are_not_configured()
    {
        await using var app = await CreateAppAsync(null, null, null);
        using var client = app.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/operations");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", "not-a-valid-token");

        var response = await client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<WebApplication> CreateAppAsync(
        string? authority,
        string? audience,
        SecurityKey? trustedKey)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var values = new Dictionary<string, string?>();
        if (authority is not null) values["MonoOperations:Authority"] = authority;
        if (audience is not null) values["MonoOperations:Audience"] = audience;
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddMonoOperationsAuthentication(builder.Configuration);
        if (trustedKey is not null)
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = authority };
            configuration.SigningKeys.Add(trustedKey);
            builder.Services.PostConfigure<JwtBearerOptions>(MonoOperationsDefaults.Scheme, options =>
            {
                options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
                options.TokenValidationParameters.ValidIssuer = authority;
                options.TokenValidationParameters.ValidAudience = audience;
                options.TokenValidationParameters.IssuerSigningKey = trustedKey;
            });
        }

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/operations", (IMonoOperationsContext context) => context.ActorId)
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        await app.StartAsync();
        return app;
    }

    private static string CreateToken(
        SecurityKey key,
        string issuer,
        string audience,
        bool includeMfa = true,
        int expiresInMinutes = 10)
    {
        var claims = new List<Claim>
        {
            new(MonoOperationsClaimTypes.ActorId, "staff-1"),
            new(MonoOperationsClaimTypes.Role, MonoOperationsRoles.Administrator)
        };
        if (includeMfa)
            claims.Add(new Claim(MonoOperationsClaimTypes.MultiFactor, "true"));
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            now.AddMinutes(-1),
            now.AddMinutes(expiresInMinutes),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static Task<System.Net.Http.HttpResponseMessage> SendTokenAsync(
        HttpClient client,
        string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/operations");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }
}
