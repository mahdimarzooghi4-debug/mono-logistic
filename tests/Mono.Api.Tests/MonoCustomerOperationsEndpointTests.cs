using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
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
using Mono.Api.Api;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Npgsql;
using NpgsqlTypes;
using Xunit;

namespace Mono.Api.Tests;

[Collection("Mono PostgreSQL")]
public sealed class MonoCustomerOperationsEndpointTests
{
    private const string Authority = "https://identity.mono-test.invalid";
    private const string Audience = "mono-api-test";

    [Fact]
    public async Task Customer_onboarding_requires_admin_and_audits_activation()
    {
        var dataSource = await CreateDataSourceAsync();
        if (dataSource is null) return;
        await using (dataSource)
        using (var rsa = RSA.Create(2048))
        {
            var signingKey = new RsaSecurityKey(rsa) { KeyId = "operations-customer-test-key" };
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MonoOperations:Authority"] = Authority,
                ["MonoOperations:Audience"] = Audience
            });
            builder.Services.AddMonoOperationsAuthentication(builder.Configuration);
            builder.Services.AddSingleton(dataSource);
            builder.Services.AddScoped<MonoCustomerOperationsService>();
            builder.Services.AddScoped<MonoBranchOperationsService>();
            builder.Services.AddScoped<MonoApiKeyOperationsService>();
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
            app.MapMonoCustomerOperationsEndpoints();
            app.MapMonoBranchOperationsEndpoints();
            app.MapMonoApiKeyOperationsEndpoints();
            await app.StartAsync();
            await using (app)
            using (var client = app.GetTestClient())
            {
                var createPath = "/operations/v1/customers";
                using var unauthorizedCreate = new HttpRequestMessage(HttpMethod.Post, createPath)
                {
                    Content = JsonContent.Create(new
                    {
                        legalName = "Mono Test Merchant",
                        reason = "onboarding test"
                    })
                };
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.SendAsync(unauthorizedCreate)).StatusCode);

                using var nonAdminCreate = new HttpRequestMessage(HttpMethod.Post, createPath)
                {
                    Content = JsonContent.Create(new
                    {
                        legalName = "Mono Test Merchant",
                        reason = "onboarding test"
                    })
                };
                nonAdminCreate.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminCreate)).StatusCode);

                using var create = new HttpRequestMessage(HttpMethod.Post, createPath)
                {
                    Content = JsonContent.Create(new
                    {
                        legalName = "  Mono Test Merchant  ",
                        displayName = "Merchant Test",
                        customerType = "MERCHANT",
                        defaultChannel = "B2B_API",
                        defaultCurrency = "irt",
                        reason = "approved onboarding request"
                    })
                };
                create.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var created = await client.SendAsync(create);
                Assert.Equal(System.Net.HttpStatusCode.Created, created.StatusCode);
                using var body = System.Text.Json.JsonDocument.Parse(
                    await created.Content.ReadAsStringAsync());
                var customerId = body.RootElement.GetProperty("customerId").GetString()!;
                Assert.Equal("PENDING", body.RootElement.GetProperty("status").GetString());
                Assert.Equal("IRT", body.RootElement.GetProperty("defaultCurrency").GetString());
                Assert.DoesNotContain("HANA", body.RootElement.GetProperty("customerId").GetString()!,
                    StringComparison.OrdinalIgnoreCase);

                var activatePath = $"/operations/v1/customers/{customerId}/activate";
                using var nonAdminActivate = new HttpRequestMessage(HttpMethod.Post, activatePath)
                {
                    Content = JsonContent.Create(new { reason = "not allowed" })
                };
                nonAdminActivate.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminActivate)).StatusCode);

                using var activate = new HttpRequestMessage(HttpMethod.Post, activatePath)
                {
                    Content = JsonContent.Create(new { reason = "contract signed and verified" })
                };
                activate.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.NoContent,
                    (await client.SendAsync(activate)).StatusCode);
                var branchPath = $"/operations/v1/customers/{customerId}/branches";
                using var unauthorizedBranch = new HttpRequestMessage(HttpMethod.Post, branchPath)
                {
                    Content = JsonContent.Create(new
                    {
                        name = "Test branch",
                        reason = "onboarding test"
                    })
                };
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.SendAsync(unauthorizedBranch)).StatusCode);

                using var nonAdminBranch = new HttpRequestMessage(HttpMethod.Post, branchPath)
                {
                    Content = JsonContent.Create(new
                    {
                        name = "Test branch",
                        reason = "onboarding test"
                    })
                };
                nonAdminBranch.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminBranch)).StatusCode);

                using var emptyBranchListRequest = new HttpRequestMessage(HttpMethod.Get, branchPath);
                emptyBranchListRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var emptyBranchListResponse = await client.SendAsync(emptyBranchListRequest);
                Assert.Equal(System.Net.HttpStatusCode.OK, emptyBranchListResponse.StatusCode);
                using var emptyBranchListBody = System.Text.Json.JsonDocument.Parse(
                    await emptyBranchListResponse.Content.ReadAsStringAsync());
                Assert.Empty(emptyBranchListBody.RootElement.GetProperty("branches").EnumerateArray());

                using var createBranch = new HttpRequestMessage(HttpMethod.Post, branchPath)
                {
                    Content = JsonContent.Create(new
                    {
                        name = "  Central Branch  ",
                        address = "Main street",
                        latitude = 35.7,
                        longitude = 51.4,
                        zoneId = "zone-central",
                        operatingHours = new { note = "structure is retained; schema is not inferred" },
                        reason = "branch verified for onboarding"
                    })
                };
                createBranch.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var createdBranch = await client.SendAsync(createBranch);
                Assert.Equal(System.Net.HttpStatusCode.Created, createdBranch.StatusCode);
                using var branchBody = System.Text.Json.JsonDocument.Parse(
                    await createdBranch.Content.ReadAsStringAsync());
                var branchId = branchBody.RootElement.GetProperty("branchId").GetString()!;
                Assert.StartsWith("br_", branchId);
                Assert.Equal("ACTIVE", branchBody.RootElement.GetProperty("status").GetString());

                using var unauthorizedBranchList = new HttpRequestMessage(HttpMethod.Get, branchPath);
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.SendAsync(unauthorizedBranchList)).StatusCode);

                using var nonAdminBranchList = new HttpRequestMessage(HttpMethod.Get, branchPath);
                nonAdminBranchList.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminBranchList)).StatusCode);

                using var branchListRequest = new HttpRequestMessage(HttpMethod.Get, branchPath);
                branchListRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var branchListResponse = await client.SendAsync(branchListRequest);
                Assert.Equal(System.Net.HttpStatusCode.OK, branchListResponse.StatusCode);
                using var branchListBody = System.Text.Json.JsonDocument.Parse(
                    await branchListResponse.Content.ReadAsStringAsync());
                var listedBranches = branchListBody.RootElement.GetProperty("branches");
                var listedBranch = Assert.Single(listedBranches.EnumerateArray());
                Assert.Equal(branchId, listedBranch.GetProperty("branchId").GetString());
                Assert.Equal(customerId, listedBranch.GetProperty("customerId").GetString());
                Assert.Equal("Central Branch", listedBranch.GetProperty("name").GetString());
                Assert.Equal("ACTIVE", listedBranch.GetProperty("status").GetString());

                using var missingCustomerBranchList = new HttpRequestMessage(
                    HttpMethod.Get,
                    "/operations/v1/customers/cus_00000000000000000000000000000000/branches");
                missingCustomerBranchList.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.NotFound,
                    (await client.SendAsync(missingCustomerBranchList)).StatusCode);

                using var invalidCoordinates = new HttpRequestMessage(HttpMethod.Post, branchPath)
                {
                    Content = JsonContent.Create(new
                    {
                        name = "Invalid branch",
                        latitude = 91,
                        longitude = 51,
                        reason = "coordinates validation"
                    })
                };
                invalidCoordinates.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.BadRequest,
                    (await client.SendAsync(invalidCoordinates)).StatusCode);

                var branchItemPath = branchPath + "/" + branchId;
                using var unauthorizedBranchUpdate = new HttpRequestMessage(HttpMethod.Put, branchItemPath)
                {
                    Content = JsonContent.Create(new
                    {
                        name = "Updated branch",
                        reason = "unauthenticated update"
                    })
                };
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.SendAsync(unauthorizedBranchUpdate)).StatusCode);

                using var nonAdminBranchUpdate = new HttpRequestMessage(HttpMethod.Put, branchItemPath)
                {
                    Content = JsonContent.Create(new
                    {
                        name = "Updated branch",
                        reason = "unauthorized update"
                    })
                };
                nonAdminBranchUpdate.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminBranchUpdate)).StatusCode);

                using var wrongCustomerUpdate = new HttpRequestMessage(
                    HttpMethod.Put,
                    "/operations/v1/customers/cus_00000000000000000000000000000000/branches/" + branchId)
                {
                    Content = JsonContent.Create(new
                    {
                        name = "Wrong tenant",
                        reason = "tenant isolation"
                    })
                };
                wrongCustomerUpdate.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.NotFound,
                    (await client.SendAsync(wrongCustomerUpdate)).StatusCode);

                using var updateBranch = new HttpRequestMessage(HttpMethod.Put, branchItemPath)
                {
                    Content = JsonContent.Create(new
                    {
                        name = "  Central Branch Updated  ",
                        address = "Second street",
                        latitude = 35.71,
                        longitude = 51.41,
                        zoneId = "zone-north",
                        operatingHours = new { note = "replaced schedule object" },
                        reason = "verified address correction"
                    })
                };
                updateBranch.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var updatedBranchResponse = await client.SendAsync(updateBranch);
                Assert.Equal(System.Net.HttpStatusCode.OK, updatedBranchResponse.StatusCode);
                using var updatedBranchBody = System.Text.Json.JsonDocument.Parse(
                    await updatedBranchResponse.Content.ReadAsStringAsync());
                Assert.Equal("Central Branch Updated",
                    updatedBranchBody.RootElement.GetProperty("name").GetString());

                using var invalidBranchUpdate = new HttpRequestMessage(HttpMethod.Put, branchItemPath)
                {
                    Content = JsonContent.Create(new
                    {
                        name = "Must not persist",
                        latitude = 91,
                        longitude = 51,
                        reason = "invalid update"
                    })
                };
                invalidBranchUpdate.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.BadRequest,
                    (await client.SendAsync(invalidBranchUpdate)).StatusCode);

                using var repeatedActivate = new HttpRequestMessage(HttpMethod.Post, activatePath)
                {
                    Content = JsonContent.Create(new { reason = "duplicate activation" })
                };
                repeatedActivate.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.Conflict,
                    (await client.SendAsync(repeatedActivate)).StatusCode);

                await using var connection = await dataSource.OpenConnectionAsync();
                await using var command = new NpgsqlCommand("""
                    SELECT c.status, a.action, a.actor_id, a.reason
                    FROM customers c
                    JOIN customer_operations_audit a ON a.customer_id = c.customer_id
                    WHERE c.customer_id = @customer_id
                    ORDER BY a.occurred_at, a.audit_id;
                    """, connection);
                command.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
                await using var reader = await command.ExecuteReaderAsync();
                var audit = new List<(string Status, string Action, string Actor, string Reason)>();
                while (await reader.ReadAsync())
                    audit.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
                Assert.Equal("ACTIVE", audit[^1].Status);
                Assert.Equal(new[] { "CREATED", "ACTIVATED" }, audit.Select(x => x.Action));
                Assert.All(audit, entry => Assert.Equal("admin-test", entry.Actor));
                Assert.Equal("approved onboarding request", audit[0].Reason);
                Assert.Equal("contract signed and verified", audit[1].Reason);

                await using var branchConnection = await dataSource.OpenConnectionAsync();
                await using var branchCommand = new NpgsqlCommand("""
                    SELECT b.name, b.latitude, b.longitude, a.action, a.reason
                    FROM branches b
                    JOIN branch_operations_audit a ON a.branch_id = b.branch_id
                    WHERE b.branch_id = @branch_id
                    ORDER BY a.occurred_at, a.audit_id;
                    """, branchConnection);
                branchCommand.Parameters.AddWithValue("branch_id", NpgsqlDbType.Text, branchId);
                await using var branchReader = await branchCommand.ExecuteReaderAsync();
                var branchHistory = new List<(string Name, double? Latitude, double? Longitude, string Action, string Reason)>();
                while (await branchReader.ReadAsync())
                    branchHistory.Add((
                        branchReader.GetString(0),
                        branchReader.IsDBNull(1) ? null : branchReader.GetDouble(1),
                        branchReader.IsDBNull(2) ? null : branchReader.GetDouble(2),
                        branchReader.GetString(3),
                        branchReader.GetString(4)));
                Assert.Equal("Central Branch Updated", branchHistory[^1].Name);
                Assert.Equal(35.71, branchHistory[^1].Latitude);
                Assert.Equal(51.41, branchHistory[^1].Longitude);
                Assert.Equal(new[] { "CREATED", "UPDATED" }, branchHistory.Select(x => x.Action));
                Assert.Equal("verified address correction", branchHistory[^1].Reason);

                var apiKeysPath = $"/operations/v1/customers/{customerId}/api-keys";
                using var emptyKeyListRequest = new HttpRequestMessage(HttpMethod.Get, apiKeysPath);
                emptyKeyListRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var emptyKeyListResponse = await client.SendAsync(emptyKeyListRequest);
                Assert.Equal(System.Net.HttpStatusCode.OK, emptyKeyListResponse.StatusCode);
                using var emptyKeyListBody = System.Text.Json.JsonDocument.Parse(
                    await emptyKeyListResponse.Content.ReadAsStringAsync());
                Assert.Empty(emptyKeyListBody.RootElement.GetProperty("apiKeys").EnumerateArray());

                using var unauthorizedKeyListRequest = new HttpRequestMessage(HttpMethod.Get, apiKeysPath);
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.SendAsync(unauthorizedKeyListRequest)).StatusCode);
                using var nonAdminKeyListRequest = new HttpRequestMessage(HttpMethod.Get, apiKeysPath);
                nonAdminKeyListRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminKeyListRequest)).StatusCode);

                var issuedAt = DateTimeOffset.UtcNow;
                var issuedKey = MonoApiKeyIssuer.Create(
                    customerId, ["branches.read"], issuedAt.AddDays(7), issuedAt);
                await new PostgresApiKeyProvisioner(dataSource).CreateAsync(issuedKey.Credential);
                using var keyListRequest = new HttpRequestMessage(HttpMethod.Get, apiKeysPath);
                keyListRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var keyListResponse = await client.SendAsync(keyListRequest);
                Assert.Equal(System.Net.HttpStatusCode.OK, keyListResponse.StatusCode);
                using var keyListBody = System.Text.Json.JsonDocument.Parse(
                    await keyListResponse.Content.ReadAsStringAsync());
                var listedKeys = keyListBody.RootElement.GetProperty("apiKeys");
                var listedKey = Assert.Single(listedKeys.EnumerateArray());
                Assert.Equal(issuedKey.Credential.KeyId, listedKey.GetProperty("keyId").GetString());
                Assert.Equal(customerId, listedKey.GetProperty("customerId").GetString());
                Assert.Equal("branches.read",
                    Assert.Single(listedKey.GetProperty("scopes").EnumerateArray()).GetString());
                var listedExpiry = listedKey.GetProperty("expiresAt").GetDateTimeOffset();
                Assert.InRange(
                    (issuedKey.Credential.ExpiresAt - listedExpiry).Duration(),
                    TimeSpan.Zero,
                    TimeSpan.FromMicroseconds(1));
                Assert.False(listedKey.TryGetProperty("secret", out _));
                Assert.False(listedKey.TryGetProperty("secretHash", out _));

                var revokePath = $"/operations/v1/customers/{customerId}/api-keys/{issuedKey.Credential.KeyId}/revoke";

                using var unauthenticatedRevoke = new HttpRequestMessage(HttpMethod.Post, revokePath)
                {
                    Content = JsonContent.Create(new { reason = "security rotation" })
                };
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.SendAsync(unauthenticatedRevoke)).StatusCode);

                using var nonAdminRevoke = new HttpRequestMessage(HttpMethod.Post, revokePath)
                {
                    Content = JsonContent.Create(new { reason = "security rotation" })
                };
                nonAdminRevoke.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminRevoke)).StatusCode);

                using var wrongCustomerRevoke = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"/operations/v1/customers/cus_00000000000000000000000000000000/api-keys/{issuedKey.Credential.KeyId}/revoke")
                {
                    Content = JsonContent.Create(new { reason = "tenant boundary test" })
                };
                wrongCustomerRevoke.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.NotFound,
                    (await client.SendAsync(wrongCustomerRevoke)).StatusCode);

                using var revoke = new HttpRequestMessage(HttpMethod.Post, revokePath)
                {
                    Content = JsonContent.Create(new { reason = "customer credential rotation" })
                };
                revoke.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var revokedResponse = await client.SendAsync(revoke);
                Assert.Equal(System.Net.HttpStatusCode.OK, revokedResponse.StatusCode);
                using var revokedBody = System.Text.Json.JsonDocument.Parse(
                    await revokedResponse.Content.ReadAsStringAsync());
                Assert.Equal(issuedKey.Credential.KeyId,
                    revokedBody.RootElement.GetProperty("keyId").GetString());
                Assert.False(revokedBody.RootElement.TryGetProperty("secret", out _));

                var storedCredential = await new PostgresApiKeyCredentialStore(dataSource)
                    .FindAsync(issuedKey.Credential.KeyId, CancellationToken.None);
                Assert.NotNull(storedCredential);
                Assert.NotNull(storedCredential.RevokedAt);
                Assert.False(ApiKeyCredentialVerifier.TryValidate(
                    issuedKey.Value, storedCredential, DateTimeOffset.UtcNow, out _));

                using var keyListAfterRevokeRequest = new HttpRequestMessage(HttpMethod.Get, apiKeysPath);
                keyListAfterRevokeRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var keyListAfterRevokeResponse = await client.SendAsync(keyListAfterRevokeRequest);
                using var keyListAfterRevokeBody = System.Text.Json.JsonDocument.Parse(
                    await keyListAfterRevokeResponse.Content.ReadAsStringAsync());
                var revokedListedKey = Assert.Single(
                    keyListAfterRevokeBody.RootElement.GetProperty("apiKeys").EnumerateArray());
                Assert.Equal(issuedKey.Credential.KeyId, revokedListedKey.GetProperty("keyId").GetString());
                Assert.NotEqual(System.Text.Json.JsonValueKind.Null,
                    revokedListedKey.GetProperty("revokedAt").ValueKind);

                using var repeatedRevoke = new HttpRequestMessage(HttpMethod.Post, revokePath)
                {
                    Content = JsonContent.Create(new { reason = "duplicate revocation" })
                };
                repeatedRevoke.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.Conflict,
                    (await client.SendAsync(repeatedRevoke)).StatusCode);

                await using var keyAuditConnection = await dataSource.OpenConnectionAsync();
                await using var keyAuditCommand = new NpgsqlCommand("""
                    SELECT action, actor_id, reason
                    FROM customer_api_key_operations_audit
                    WHERE key_id = @key_id AND customer_id = @customer_id;
                    """, keyAuditConnection);
                keyAuditCommand.Parameters.AddWithValue("key_id", NpgsqlDbType.Text, issuedKey.Credential.KeyId);
                keyAuditCommand.Parameters.AddWithValue("customer_id", NpgsqlDbType.Text, customerId);
                await using var keyAuditReader = await keyAuditCommand.ExecuteReaderAsync();
                Assert.True(await keyAuditReader.ReadAsync());
                Assert.Equal("REVOKED", keyAuditReader.GetString(0));
                Assert.Equal("admin-test", keyAuditReader.GetString(1));
                Assert.Equal("customer credential rotation", keyAuditReader.GetString(2));
                Assert.False(await keyAuditReader.ReadAsync());
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

    private static async Task<NpgsqlDataSource?> CreateDataSourceAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("MONO_TEST_DATABASE");
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        var dataSource = NpgsqlDataSource.Create(connectionString);
        var assembly = typeof(MonoCustomerOperationsEndpointTests).Assembly;
        foreach (var name in new[]
        {
            "Mono.Api.Tests.Migrations.001_customer_branch.sql",
            "Mono.Api.Tests.Migrations.002_customer_api_keys.sql",
            "Mono.Api.Tests.Migrations.003_delivery_request_intake.sql",
            "Mono.Api.Tests.Migrations.004_delivery_request_list_index.sql",
            "Mono.Api.Tests.Migrations.005_customer_service_configurations.sql",
            "Mono.Api.Tests.Migrations.006_customer_service_configuration_audit.sql",
            "Mono.Api.Tests.Migrations.007_customer_service_configuration_retirement.sql",
            "Mono.Api.Tests.Migrations.008_customer_operations_audit.sql",
            "Mono.Api.Tests.Migrations.009_branch_operations_audit.sql",
            "Mono.Api.Tests.Migrations.010_branch_operations_audit_update.sql",
            "Mono.Api.Tests.Migrations.011_customer_api_key_operations_audit.sql"
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
