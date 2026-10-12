using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Mono.Api.Api;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class CustomerServiceConfigurationInspectionEndpointTests
{
    private const string Authority = "https://identity.mono-test.invalid";
    private const string Audience = "mono-api-test";

    [Fact]
    public async Task Operations_api_requires_mono_admin_and_returns_metadata_and_audit_only()
    {
        var dataSource = await CreateDataSourceAsync();
        if (dataSource is null) return;
        await using (dataSource)
        {
            var customerId = "cus_" + Guid.NewGuid().ToString("N");
            await using (var connection = await dataSource.OpenConnectionAsync())
            await using (var insert = new NpgsqlCommand("""
                INSERT INTO customers (customer_id, legal_name, status, created_at)
                VALUES (@id, 'Mono Operations API Test', 'ACTIVE', @at);
                """, connection))
            {
                insert.Parameters.AddWithValue("id", NpgsqlDbType.Text, customerId);
                insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow);
                await insert.ExecuteNonQueryAsync();
            }

            var authoring = new CustomerServiceConfigurationAuthoringService(
                dataSource, new FixedOperationsContext("operator-test", [MonoOperationsRoles.Administrator]));
            using var configuration = JsonDocument.Parse("{\"secretInternalValue\":\"must-not-be-returned\"}");
            await authoring.CreateDraftAsync(customerId, "URBAN_DELIVERY",
                DateTimeOffset.UtcNow, null, configuration.RootElement, "integration test setup");

            using var rsa = RSA.Create(2048);
            var signingKey = new RsaSecurityKey(rsa) { KeyId = "operations-test-key" };
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MonoOperations:Authority"] = Authority,
                ["MonoOperations:Audience"] = Audience
            });
            builder.Services.AddMonoOperationsAuthentication(builder.Configuration);
            builder.Services.AddSingleton(dataSource);
            builder.Services.AddScoped<CustomerServiceConfigurationInspectionService>();
            var oidcConfiguration = new OpenIdConnectConfiguration { Issuer = Authority };
            oidcConfiguration.SigningKeys.Add(signingKey);
            builder.Services.PostConfigure<JwtBearerOptions>(MonoOperationsDefaults.Scheme, options =>
            {
                options.ConfigurationManager =
                    new StaticConfigurationManager<OpenIdConnectConfiguration>(oidcConfiguration);
                options.TokenValidationParameters.ValidIssuer = Authority;
                options.TokenValidationParameters.ValidAudience = Audience;
                options.TokenValidationParameters.IssuerSigningKey = signingKey;
            });

            var app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapCustomerServiceConfigurationInspectionEndpoints();
            await app.StartAsync();
            await using (app)
            using (var client = app.GetTestClient())
            {
                var path = $"/operations/v1/customers/{customerId}/services/URBAN_DELIVERY";
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.GetAsync(path + "/configurations")).StatusCode);

                using var operatorRequest = new HttpRequestMessage(HttpMethod.Get, path + "/configurations");
                operatorRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(operatorRequest)).StatusCode);

                using var adminRequest = new HttpRequestMessage(HttpMethod.Get, path + "/configurations");
                adminRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var versionsResponse = await client.SendAsync(adminRequest);
                Assert.Equal(System.Net.HttpStatusCode.OK, versionsResponse.StatusCode);
                var versionsBody = await versionsResponse.Content.ReadAsStringAsync();
                Assert.Contains("\"serviceCode\":\"URBAN_DELIVERY\"", versionsBody);
                Assert.Contains("\"status\":\"DRAFT\"", versionsBody);
                Assert.DoesNotContain("secretInternalValue", versionsBody);

                using var auditRequest = new HttpRequestMessage(HttpMethod.Get, path + "/configuration-audit");
                auditRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var auditResponse = await client.SendAsync(auditRequest);
                Assert.Equal(System.Net.HttpStatusCode.OK, auditResponse.StatusCode);
                var auditBody = await auditResponse.Content.ReadAsStringAsync();
                Assert.Contains("\"action\":\"DRAFT_CREATED\"", auditBody);
                Assert.Contains("\"actorId\":\"operator-test\"", auditBody);

                using var invalidLimitRequest = new HttpRequestMessage(
                    HttpMethod.Get, path + "/configurations?limit=101");
                invalidLimitRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var invalidLimitResponse = await client.SendAsync(invalidLimitRequest);
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalidLimitResponse.StatusCode);
                Assert.Contains("INVALID_REQUEST",
                    await invalidLimitResponse.Content.ReadAsStringAsync());
            }
        }
    }

    private static string CreateToken(SecurityKey signingKey, string actorId, string role, bool includeMfa = true)
    {
        var claims = new List<Claim>
        {
            new(MonoOperationsClaimTypes.ActorId, actorId),
            new(MonoOperationsClaimTypes.Role, role)
        };
        if (includeMfa)
            claims.Add(new Claim(MonoOperationsClaimTypes.MultiFactor, "true"));
        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            Authority,
            Audience,
            claims,
            now.AddMinutes(-1),
            now.AddMinutes(10),
            new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record FixedOperationsContext(string ActorId, IReadOnlyCollection<string> Roles)
        : IMonoOperationsContext;

    private static async Task<NpgsqlDataSource?> CreateDataSourceAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        var dataSource = NpgsqlDataSource.Create(connectionString);
        var assembly = typeof(CustomerServiceConfigurationInspectionEndpointTests).Assembly;
        foreach (var name in new[]
        {
            "Mono.Api.Tests.Migrations.001_customer_branch.sql",
            "Mono.Api.Tests.Migrations.002_customer_api_keys.sql",
            "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
            "Mono.Api.Tests.Migrations.004_delivery_request_list_index.sql",
            "Mono.Api.Tests.Migrations.005_customer_service_configurations.sql",
            "Mono.Api.Tests.Migrations.006_customer_service_configuration_audit.sql"
        })
        {
            await using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException("Missing embedded migration resource: " + name);
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync();
            await using var connection = await dataSource.OpenConnectionAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        return dataSource;
    }
}
