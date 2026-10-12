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
            builder.Services.AddScoped<MonoCustomerExternalIdentifierOperationsService>();
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

                var externalIdentifiersPath =
                    $"/operations/v1/customers/{customerId}/external-identifiers";
                using var unauthenticatedIdentifiers = new HttpRequestMessage(
                    HttpMethod.Get, externalIdentifiersPath);
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.SendAsync(unauthenticatedIdentifiers)).StatusCode);
                using var nonAdminIdentifiers = new HttpRequestMessage(
                    HttpMethod.Get, externalIdentifiersPath);
                nonAdminIdentifiers.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminIdentifiers)).StatusCode);

                using var emptyIdentifiers = new HttpRequestMessage(
                    HttpMethod.Get, externalIdentifiersPath);
                emptyIdentifiers.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var emptyIdentifiersResponse = await client.SendAsync(emptyIdentifiers);
                Assert.Equal(System.Net.HttpStatusCode.OK, emptyIdentifiersResponse.StatusCode);
                using var emptyIdentifiersBody = System.Text.Json.JsonDocument.Parse(
                    await emptyIdentifiersResponse.Content.ReadAsStringAsync());
                Assert.Empty(emptyIdentifiersBody.RootElement.GetProperty("identifiers").EnumerateArray());

                using var addExternalIdentifier = new HttpRequestMessage(
                    HttpMethod.Post, externalIdentifiersPath)
                {
                    Content = JsonContent.Create(new
                    {
                        @namespace = " HANA ",
                        externalId = "merchant-001",
                        reason = "verified first-party customer mapping"
                    })
                };
                addExternalIdentifier.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var addedExternalIdentifier = await client.SendAsync(addExternalIdentifier);
                Assert.Equal(System.Net.HttpStatusCode.Created, addedExternalIdentifier.StatusCode);
                using var addedExternalIdentifierBody = System.Text.Json.JsonDocument.Parse(
                    await addedExternalIdentifier.Content.ReadAsStringAsync());
                Assert.Equal("hana",
                    addedExternalIdentifierBody.RootElement.GetProperty("namespace").GetString());
                Assert.Equal("merchant-001",
                    addedExternalIdentifierBody.RootElement.GetProperty("externalId").GetString());

                var externalIdentifierId =
                    addedExternalIdentifierBody.RootElement.GetProperty("identifierId").GetString()!;

                using var externalIdentifierAudit = new HttpRequestMessage(
                    HttpMethod.Get, externalIdentifiersPath + "/audit");
                externalIdentifierAudit.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var externalIdentifierAuditResponse = await client.SendAsync(externalIdentifierAudit);
                Assert.Equal(System.Net.HttpStatusCode.OK, externalIdentifierAuditResponse.StatusCode);
                using var externalIdentifierAuditBody = System.Text.Json.JsonDocument.Parse(
                    await externalIdentifierAuditResponse.Content.ReadAsStringAsync());
                var externalIdentifierAuditEntry = Assert.Single(
                    externalIdentifierAuditBody.RootElement.GetProperty("entries").EnumerateArray());
                Assert.Equal("ADDED", externalIdentifierAuditEntry.GetProperty("action").GetString());
                Assert.Equal("admin-test", externalIdentifierAuditEntry.GetProperty("actorId").GetString());
                Assert.Equal("verified first-party customer mapping",
                    externalIdentifierAuditEntry.GetProperty("reason").GetString());

                using var duplicateExternalIdentifier = new HttpRequestMessage(
                    HttpMethod.Post, externalIdentifiersPath)
                {
                    Content = JsonContent.Create(new
                    {
                        @namespace = "hana",
                        externalId = "merchant-001",
                        reason = "duplicate mapping test"
                    })
                };
                duplicateExternalIdentifier.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var duplicateExternalIdentifierResponse =
                    await client.SendAsync(duplicateExternalIdentifier);
                Assert.Equal(System.Net.HttpStatusCode.Conflict,
                    duplicateExternalIdentifierResponse.StatusCode);

                using var invalidExternalNamespace = new HttpRequestMessage(
                    HttpMethod.Post, externalIdentifiersPath)
                {
                    Content = JsonContent.Create(new
                    {
                        @namespace = "not a namespace",
                        externalId = "merchant-002",
                        reason = "invalid namespace test"
                    })
                };
                invalidExternalNamespace.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var invalidExternalNamespaceResponse =
                    await client.SendAsync(invalidExternalNamespace);
                Assert.Equal(System.Net.HttpStatusCode.BadRequest,
                    invalidExternalNamespaceResponse.StatusCode);

                using var unknownCustomerIdentifiers = new HttpRequestMessage(
                    HttpMethod.Get,
                    "/operations/v1/customers/cus_00000000000000000000000000000000/external-identifiers");
                unknownCustomerIdentifiers.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.NotFound,
                    (await client.SendAsync(unknownCustomerIdentifiers)).StatusCode);

                const string customerListPath = "/operations/v1/customers";
                using var customerNameSearch = new HttpRequestMessage(
                    HttpMethod.Get, customerListPath + "?search=mono%20test%20merchant&page_size=1");
                customerNameSearch.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var customerNameSearchResponse = await client.SendAsync(customerNameSearch);
                Assert.Equal(System.Net.HttpStatusCode.OK, customerNameSearchResponse.StatusCode);
                using var customerNameSearchBody = System.Text.Json.JsonDocument.Parse(
                    await customerNameSearchResponse.Content.ReadAsStringAsync());
                var searchedCustomer = Assert.Single(
                    customerNameSearchBody.RootElement.GetProperty("customers").EnumerateArray());
                Assert.Equal(customerId, searchedCustomer.GetProperty("customerId").GetString());

                using var literalWildcardSearch = new HttpRequestMessage(
                    HttpMethod.Get, customerListPath + "?search=%25&page_size=1");
                literalWildcardSearch.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var literalWildcardResponse = await client.SendAsync(literalWildcardSearch);
                Assert.Equal(System.Net.HttpStatusCode.OK, literalWildcardResponse.StatusCode);
                using var literalWildcardBody = System.Text.Json.JsonDocument.Parse(
                    await literalWildcardResponse.Content.ReadAsStringAsync());
                Assert.Empty(literalWildcardBody.RootElement.GetProperty("customers").EnumerateArray());

                using var invalidSearch = new HttpRequestMessage(
                    HttpMethod.Get, customerListPath + "?search=" + new string('a', 129));
                invalidSearch.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var invalidSearchResponse = await client.SendAsync(invalidSearch);
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalidSearchResponse.StatusCode);

                using var unauthenticatedCustomerList = new HttpRequestMessage(HttpMethod.Get, customerListPath);
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.SendAsync(unauthenticatedCustomerList)).StatusCode);
                using var nonAdminCustomerList = new HttpRequestMessage(HttpMethod.Get, customerListPath);
                nonAdminCustomerList.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminCustomerList)).StatusCode);

                using var invalidPageSize = new HttpRequestMessage(HttpMethod.Get, customerListPath + "?page_size=0");
                invalidPageSize.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var invalidPageSizeResponse = await client.SendAsync(invalidPageSize);
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalidPageSizeResponse.StatusCode);

                using var invalidStatus = new HttpRequestMessage(
                    HttpMethod.Get, customerListPath + "?status=SUSPENDED");
                invalidStatus.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var invalidStatusResponse = await client.SendAsync(invalidStatus);
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalidStatusResponse.StatusCode);

                using var invalidCursor = new HttpRequestMessage(HttpMethod.Get, customerListPath + "?cursor=invalid");
                invalidCursor.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var invalidCursorResponse = await client.SendAsync(invalidCursor);
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalidCursorResponse.StatusCode);
                using var unknownCursor = new HttpRequestMessage(
                    HttpMethod.Get,
                    customerListPath + "?cursor=cus_00000000000000000000000000000000");
                unknownCursor.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var unknownCursorResponse = await client.SendAsync(unknownCursor);
                Assert.Equal(System.Net.HttpStatusCode.BadRequest, unknownCursorResponse.StatusCode);

                using var secondCreate = new HttpRequestMessage(HttpMethod.Post, createPath)
                {
                    Content = JsonContent.Create(new
                    {
                        legalName = "Second Mono Test Merchant",
                        reason = "registry pagination test"
                    })
                };
                secondCreate.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var secondCreated = await client.SendAsync(secondCreate);
                Assert.Equal(System.Net.HttpStatusCode.Created, secondCreated.StatusCode);
                using var secondCreatedBody = System.Text.Json.JsonDocument.Parse(
                    await secondCreated.Content.ReadAsStringAsync());
                var secondCustomerId = secondCreatedBody.RootElement.GetProperty("customerId").GetString()!;

                using var crossCustomerDuplicateIdentifier = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"/operations/v1/customers/{secondCustomerId}/external-identifiers")
                {
                    Content = JsonContent.Create(new
                    {
                        @namespace = "hana",
                        externalId = "merchant-001",
                        reason = "cross-customer uniqueness test"
                    })
                };
                crossCustomerDuplicateIdentifier.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.Conflict,
                    (await client.SendAsync(crossCustomerDuplicateIdentifier)).StatusCode);

                var retireExternalIdentifierPath =
                    externalIdentifiersPath + "/" + externalIdentifierId + "/retire";
                using var unauthenticatedRetire = new HttpRequestMessage(
                    HttpMethod.Post, retireExternalIdentifierPath)
                {
                    Content = JsonContent.Create(new { reason = "retirement authorization test" })
                };
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.SendAsync(unauthenticatedRetire)).StatusCode);

                using var retireExternalIdentifier = new HttpRequestMessage(
                    HttpMethod.Post, retireExternalIdentifierPath)
                {
                    Content = JsonContent.Create(new { reason = "source account reassigned" })
                };
                retireExternalIdentifier.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var retireExternalIdentifierResponse =
                    await client.SendAsync(retireExternalIdentifier);
                Assert.Equal(System.Net.HttpStatusCode.NoContent, retireExternalIdentifierResponse.StatusCode);

                using var retiredExternalIdentifiers = new HttpRequestMessage(
                    HttpMethod.Get, externalIdentifiersPath);
                retiredExternalIdentifiers.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var retiredExternalIdentifiersResponse =
                    await client.SendAsync(retiredExternalIdentifiers);
                using var retiredExternalIdentifiersBody = System.Text.Json.JsonDocument.Parse(
                    await retiredExternalIdentifiersResponse.Content.ReadAsStringAsync());
                Assert.Empty(retiredExternalIdentifiersBody.RootElement.GetProperty("identifiers").EnumerateArray());

                using var addReassignedIdentifier = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"/operations/v1/customers/{secondCustomerId}/external-identifiers")
                {
                    Content = JsonContent.Create(new
                    {
                        @namespace = "hana",
                        externalId = "merchant-001",
                        reason = "verified account transfer"
                    })
                };
                addReassignedIdentifier.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var reassignedIdentifierResponse = await client.SendAsync(addReassignedIdentifier);
                Assert.Equal(System.Net.HttpStatusCode.Created, reassignedIdentifierResponse.StatusCode);

                using var auditAfterRetirement = new HttpRequestMessage(
                    HttpMethod.Get, externalIdentifiersPath + "/audit");
                auditAfterRetirement.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var auditAfterRetirementResponse = await client.SendAsync(auditAfterRetirement);
                using var auditAfterRetirementBody = System.Text.Json.JsonDocument.Parse(
                    await auditAfterRetirementResponse.Content.ReadAsStringAsync());
                var identifierAuditEntries =
                    auditAfterRetirementBody.RootElement.GetProperty("entries").EnumerateArray().ToArray();
                Assert.Equal(2, identifierAuditEntries.Length);
                Assert.Equal("ADDED", identifierAuditEntries[0].GetProperty("action").GetString());
                Assert.Equal("RETIRED", identifierAuditEntries[1].GetProperty("action").GetString());
                Assert.Equal("source account reassigned",
                    identifierAuditEntries[1].GetProperty("reason").GetString());

                using var firstCustomerPageRequest = new HttpRequestMessage(
                    HttpMethod.Get, customerListPath + "?page_size=1");
                firstCustomerPageRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var firstCustomerPageResponse = await client.SendAsync(firstCustomerPageRequest);
                Assert.Equal(System.Net.HttpStatusCode.OK, firstCustomerPageResponse.StatusCode);
                using var firstCustomerPage = System.Text.Json.JsonDocument.Parse(
                    await firstCustomerPageResponse.Content.ReadAsStringAsync());
                var firstPageCustomer = Assert.Single(
                    firstCustomerPage.RootElement.GetProperty("customers").EnumerateArray());
                Assert.Equal(secondCustomerId, firstPageCustomer.GetProperty("customerId").GetString());
                var nextCursor = firstCustomerPage.RootElement.GetProperty("nextCursor").GetString();
                Assert.Equal(secondCustomerId, nextCursor);

                using var nextCustomerPageRequest = new HttpRequestMessage(
                    HttpMethod.Get, customerListPath + "?page_size=1&cursor=" + nextCursor);
                nextCustomerPageRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var nextCustomerPageResponse = await client.SendAsync(nextCustomerPageRequest);
                Assert.Equal(System.Net.HttpStatusCode.OK, nextCustomerPageResponse.StatusCode);
                using var nextCustomerPage = System.Text.Json.JsonDocument.Parse(
                    await nextCustomerPageResponse.Content.ReadAsStringAsync());
                var secondPageCustomer = Assert.Single(
                    nextCustomerPage.RootElement.GetProperty("customers").EnumerateArray());
                Assert.Equal(customerId, secondPageCustomer.GetProperty("customerId").GetString());

                using var activateSecondCustomer = new HttpRequestMessage(
                    HttpMethod.Post, $"/operations/v1/customers/{secondCustomerId}/activate")
                {
                    Content = JsonContent.Create(new { reason = "status filter test" })
                };
                activateSecondCustomer.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var activateSecondCustomerResponse = await client.SendAsync(activateSecondCustomer);
                Assert.Equal(System.Net.HttpStatusCode.NoContent, activateSecondCustomerResponse.StatusCode);

                using var activeCustomerList = new HttpRequestMessage(
                    HttpMethod.Get, customerListPath + "?status=ACTIVE&page_size=1");
                activeCustomerList.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var activeCustomerListResponse = await client.SendAsync(activeCustomerList);
                Assert.Equal(System.Net.HttpStatusCode.OK, activeCustomerListResponse.StatusCode);
                using var activeCustomerListBody = System.Text.Json.JsonDocument.Parse(
                    await activeCustomerListResponse.Content.ReadAsStringAsync());
                var listedActiveCustomer = Assert.Single(
                    activeCustomerListBody.RootElement.GetProperty("customers").EnumerateArray());
                Assert.Equal(secondCustomerId, listedActiveCustomer.GetProperty("customerId").GetString());
                Assert.Equal("ACTIVE", listedActiveCustomer.GetProperty("status").GetString());

                using var pendingCustomerList = new HttpRequestMessage(
                    HttpMethod.Get, customerListPath + "?status=pending&page_size=1");
                pendingCustomerList.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var pendingCustomerListResponse = await client.SendAsync(pendingCustomerList);
                Assert.Equal(System.Net.HttpStatusCode.OK, pendingCustomerListResponse.StatusCode);
                using var pendingCustomerListBody = System.Text.Json.JsonDocument.Parse(
                    await pendingCustomerListResponse.Content.ReadAsStringAsync());
                var listedPendingCustomer = Assert.Single(
                    pendingCustomerListBody.RootElement.GetProperty("customers").EnumerateArray());
                Assert.Equal(customerId, listedPendingCustomer.GetProperty("customerId").GetString());
                Assert.Equal("PENDING", listedPendingCustomer.GetProperty("status").GetString());

                var customerPath = $"/operations/v1/customers/{customerId}";
                using var unauthorizedCustomerRead = new HttpRequestMessage(HttpMethod.Get, customerPath);
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.SendAsync(unauthorizedCustomerRead)).StatusCode);
                using var nonAdminCustomerRead = new HttpRequestMessage(HttpMethod.Get, customerPath);
                nonAdminCustomerRead.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminCustomerRead)).StatusCode);

                using var customerRead = new HttpRequestMessage(HttpMethod.Get, customerPath);
                customerRead.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var customerReadResponse = await client.SendAsync(customerRead);
                Assert.Equal(System.Net.HttpStatusCode.OK, customerReadResponse.StatusCode);
                using var customerReadBody = System.Text.Json.JsonDocument.Parse(
                    await customerReadResponse.Content.ReadAsStringAsync());
                Assert.Equal(customerId, customerReadBody.RootElement.GetProperty("customerId").GetString());
                Assert.Equal("PENDING", customerReadBody.RootElement.GetProperty("status").GetString());
                Assert.Equal("Mono Test Merchant", customerReadBody.RootElement.GetProperty("legalName").GetString());

                using var customerAuditRead = new HttpRequestMessage(HttpMethod.Get, customerPath + "/audit");
                customerAuditRead.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var customerAuditResponse = await client.SendAsync(customerAuditRead);
                Assert.Equal(System.Net.HttpStatusCode.OK, customerAuditResponse.StatusCode);
                using var customerAuditBody = System.Text.Json.JsonDocument.Parse(
                    await customerAuditResponse.Content.ReadAsStringAsync());
                var initialAuditEntry = Assert.Single(
                    customerAuditBody.RootElement.GetProperty("entries").EnumerateArray());
                Assert.Equal("CREATED", initialAuditEntry.GetProperty("action").GetString());
                Assert.Equal("admin-test", initialAuditEntry.GetProperty("actorId").GetString());
                Assert.Equal("approved onboarding request", initialAuditEntry.GetProperty("reason").GetString());

                using var missingCustomerRead = new HttpRequestMessage(
                    HttpMethod.Get,
                    "/operations/v1/customers/cus_00000000000000000000000000000000");
                missingCustomerRead.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.NotFound,
                    (await client.SendAsync(missingCustomerRead)).StatusCode);

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

                using var activeCustomerRead = new HttpRequestMessage(HttpMethod.Get, customerPath);
                activeCustomerRead.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var activeCustomerResponse = await client.SendAsync(activeCustomerRead);
                using var activeCustomerBody = System.Text.Json.JsonDocument.Parse(
                    await activeCustomerResponse.Content.ReadAsStringAsync());
                Assert.Equal("ACTIVE", activeCustomerBody.RootElement.GetProperty("status").GetString());

                using var fullCustomerAuditRead = new HttpRequestMessage(HttpMethod.Get, customerPath + "/audit");
                fullCustomerAuditRead.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var fullCustomerAuditResponse = await client.SendAsync(fullCustomerAuditRead);
                using var fullCustomerAuditBody = System.Text.Json.JsonDocument.Parse(
                    await fullCustomerAuditResponse.Content.ReadAsStringAsync());
                var lifecycleEntries = fullCustomerAuditBody.RootElement.GetProperty("entries")
                    .EnumerateArray().ToArray();
                Assert.Equal(new[] { "CREATED", "ACTIVATED" },
                    lifecycleEntries.Select(entry => entry.GetProperty("action").GetString()));
                Assert.Equal("contract signed and verified",
                    lifecycleEntries[1].GetProperty("reason").GetString());
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

                using var missingCustomerKeyListRequest = new HttpRequestMessage(
                    HttpMethod.Get,
                    "/operations/v1/customers/cus_00000000000000000000000000000000/api-keys");
                missingCustomerKeyListRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var missingCustomerKeyListResponse = await client.SendAsync(missingCustomerKeyListRequest);
                Assert.Equal(System.Net.HttpStatusCode.NotFound, missingCustomerKeyListResponse.StatusCode);

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

                var keyAuditPath = $"/operations/v1/customers/{customerId}/api-keys/{issuedKey.Credential.KeyId}/audit";
                using var unauthenticatedKeyAudit = new HttpRequestMessage(HttpMethod.Get, keyAuditPath);
                Assert.Equal(System.Net.HttpStatusCode.Unauthorized,
                    (await client.SendAsync(unauthenticatedKeyAudit)).StatusCode);
                using var nonAdminKeyAudit = new HttpRequestMessage(HttpMethod.Get, keyAuditPath);
                nonAdminKeyAudit.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "operator-test", "MONO_OPERATOR", includeMfa: false));
                Assert.Equal(System.Net.HttpStatusCode.Forbidden,
                    (await client.SendAsync(nonAdminKeyAudit)).StatusCode);
                using var emptyKeyAudit = new HttpRequestMessage(HttpMethod.Get, keyAuditPath);
                emptyKeyAudit.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var emptyKeyAuditResponse = await client.SendAsync(emptyKeyAudit);
                Assert.Equal(System.Net.HttpStatusCode.OK, emptyKeyAuditResponse.StatusCode);
                using var emptyKeyAuditBody = System.Text.Json.JsonDocument.Parse(
                    await emptyKeyAuditResponse.Content.ReadAsStringAsync());
                Assert.Empty(emptyKeyAuditBody.RootElement.GetProperty("entries").EnumerateArray());

                using var wrongCustomerKeyAudit = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"/operations/v1/customers/cus_00000000000000000000000000000000/api-keys/{issuedKey.Credential.KeyId}/audit");
                wrongCustomerKeyAudit.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                Assert.Equal(System.Net.HttpStatusCode.NotFound,
                    (await client.SendAsync(wrongCustomerKeyAudit)).StatusCode);

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

                using var revokedKeyAudit = new HttpRequestMessage(HttpMethod.Get, keyAuditPath);
                revokedKeyAudit.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Bearer", CreateToken(signingKey, "admin-test", MonoOperationsRoles.Administrator));
                using var revokedKeyAuditResponse = await client.SendAsync(revokedKeyAudit);
                Assert.Equal(System.Net.HttpStatusCode.OK, revokedKeyAuditResponse.StatusCode);
                using var revokedKeyAuditBody = System.Text.Json.JsonDocument.Parse(
                    await revokedKeyAuditResponse.Content.ReadAsStringAsync());
                var revokedKeyAuditEntry = Assert.Single(
                    revokedKeyAuditBody.RootElement.GetProperty("entries").EnumerateArray());
                Assert.Equal(issuedKey.Credential.KeyId, revokedKeyAuditEntry.GetProperty("keyId").GetString());
                Assert.Equal(customerId, revokedKeyAuditEntry.GetProperty("customerId").GetString());
                Assert.Equal("REVOKED", revokedKeyAuditEntry.GetProperty("action").GetString());
                Assert.Equal("admin-test", revokedKeyAuditEntry.GetProperty("actorId").GetString());
                Assert.Equal("customer credential rotation", revokedKeyAuditEntry.GetProperty("reason").GetString());
                Assert.False(revokedKeyAuditEntry.TryGetProperty("secret", out _));
                Assert.False(revokedKeyAuditEntry.TryGetProperty("secretHash", out _));

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
            "Mono.Api.Tests.Migrations.011_customer_api_key_operations_audit.sql",
            "Mono.Api.Tests.Migrations.012_customer_registry_listing_index.sql",
            "Mono.Api.Tests.Migrations.013_customer_registry_search_indexes.sql",
            "Mono.Api.Tests.Migrations.014_customer_external_identifiers.sql"
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
