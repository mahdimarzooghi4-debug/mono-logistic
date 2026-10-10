using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
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
            builder.Services.AddScoped<CustomerServiceConfigurationAuthoringService>();
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
            app.MapCustomerServiceConfigurationAuthoringEndpoints();
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

                var authoringPath =
                    $"/operations/v1/customers/{customerId}/services/URBAN_DELIVERY/configurations";
                using var unauthorizedCreate = new HttpRequestMessage(HttpMethod.Post, authoringPath)
                {
                    Content = JsonContent.Create(new
                    {
                        effectiveFrom = DateTimeOffset.UtcNow.AddHours(1),
                        effectiveTo = (DateTimeOffset?)null,
                        configuration = new { modes = new[] { "WALK" } },
                        reason = "test"
                    })
                };
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.SendAsync(unauthorizedCreate)).StatusCode);

                using var invalidCreate = new HttpRequestMessage(HttpMethod.Post, authoringPath)
                {
                    Content = JsonContent.Create(new
                    {
                        effectiveFrom = DateTimeOffset.UtcNow.AddHours(1),
                        effectiveTo = (DateTimeOffset?)null,
                        configuration = new[] { "configuration must be an object" },
                        reason = "invalid payload test"
                    })
                };
                invalidCreate.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var invalidCreateResponse = await client.SendAsync(invalidCreate);
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalidCreateResponse.StatusCode);

                var missingConfigurationId = "scf_" + Guid.NewGuid().ToString("N");
                using var missingActivation = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"/operations/v1/customers/{customerId}/service-configurations/{missingConfigurationId}/activate")
                {
                    Content = JsonContent.Create(new { reason = "missing draft test" })
                };
                missingActivation.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.NotFound,
                    (await client.SendAsync(missingActivation)).StatusCode);

                var effectiveFrom = DateTimeOffset.UtcNow.AddHours(1);
                using var createDraft = new HttpRequestMessage(HttpMethod.Post, authoringPath)
                {
                    Content = JsonContent.Create(new
                    {
                        effectiveFrom,
                        effectiveTo = (DateTimeOffset?)null,
                        configuration = new { internalValue = "do-not-echo" },
                        reason = "operations approval reference"
                    })
                };
                createDraft.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var createdDraftResponse = await client.SendAsync(createDraft);
                Assert.Equal(System.Net.HttpStatusCode.Created, createdDraftResponse.StatusCode);
                var createdDraftBody = await createdDraftResponse.Content.ReadAsStringAsync();
                Assert.DoesNotContain("do-not-echo", createdDraftBody);
                using var createdDraftDocument = JsonDocument.Parse(createdDraftBody);
                var createdDraftId = createdDraftDocument.RootElement
                    .GetProperty("serviceConfigurationId").GetString()!;

                var otherCustomerId = "cus_" + Guid.NewGuid().ToString("N");
                await using (var connection = await dataSource.OpenConnectionAsync())
                await using (var insert = new NpgsqlCommand("""
                    INSERT INTO customers (customer_id, legal_name, status, created_at)
                    VALUES (@id, 'Mono Other Customer Test', 'ACTIVE', @at);
                    """, connection))
                {
                    insert.Parameters.AddWithValue("id", NpgsqlDbType.Text, otherCustomerId);
                    insert.Parameters.AddWithValue("at", NpgsqlDbType.TimestampTz, DateTimeOffset.UtcNow);
                    await insert.ExecuteNonQueryAsync();
                }

                using var wrongCustomerActivation = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"/operations/v1/customers/{otherCustomerId}/service-configurations/{createdDraftId}/activate")
                {
                    Content = JsonContent.Create(new { reason = "wrong customer test" })
                };
                wrongCustomerActivation.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.NotFound,
                    (await client.SendAsync(wrongCustomerActivation)).StatusCode);

                using var nonAdminCreate = new HttpRequestMessage(HttpMethod.Post, authoringPath)
                {
                    Content = JsonContent.Create(new
                    {
                        effectiveFrom = DateTimeOffset.UtcNow.AddHours(2),
                        effectiveTo = (DateTimeOffset?)null,
                        configuration = new { modes = new[] { "BIKE" } },
                        reason = "not allowed"
                    })
                };
                nonAdminCreate.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminCreate)).StatusCode);

                var activatePath =
                    $"/operations/v1/customers/{customerId}/service-configurations/{createdDraftId}/activate";
                using var nonAdminActivate = new HttpRequestMessage(HttpMethod.Post, activatePath)
                {
                    Content = JsonContent.Create(new { reason = "not allowed" })
                };
                nonAdminActivate.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminActivate)).StatusCode);

                using var activateDraft = new HttpRequestMessage(HttpMethod.Post, activatePath)
                {
                    Content = JsonContent.Create(new { reason = "authorized activation" })
                };
                activateDraft.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var activatedResponse = await client.SendAsync(activateDraft);
                Assert.Equal(System.Net.HttpStatusCode.NoContent, activatedResponse.StatusCode);

                using var overlappingDraft = new HttpRequestMessage(HttpMethod.Post, authoringPath)
                {
                    Content = JsonContent.Create(new
                    {
                        effectiveFrom = effectiveFrom.AddHours(1),
                        effectiveTo = (DateTimeOffset?)null,
                        configuration = new { internalValue = "conflict" },
                        reason = "overlap test"
                    })
                };
                overlappingDraft.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var overlappingResponse = await client.SendAsync(overlappingDraft);
                Assert.Equal(System.Net.HttpStatusCode.Created, overlappingResponse.StatusCode);
                using var overlapDocument = JsonDocument.Parse(
                    await overlappingResponse.Content.ReadAsStringAsync());
                var overlappingId = overlapDocument.RootElement
                    .GetProperty("serviceConfigurationId").GetString()!;
                using var activateOverlap = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"/operations/v1/customers/{customerId}/service-configurations/{overlappingId}/activate")
                {
                    Content = JsonContent.Create(new { reason = "must conflict" })
                };
                activateOverlap.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var overlapActivationResponse = await client.SendAsync(activateOverlap);
                Assert.Equal(System.Net.HttpStatusCode.Conflict, overlapActivationResponse.StatusCode);
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
