using Mono.Api.Api;
using Mono.Api.Persistence;
using Mono.Api.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddMonoCustomerTenantAuthorization();
builder.Services.AddMonoApiKeyAuthentication(builder.Configuration);
builder.Services.AddScoped<CustomerBranchRepository>();

var app = builder.Build();

// Customer authentication is invoked by its explicit authorization policy.
// Health endpoints therefore remain independent of credential storage.
app.UseAuthorization();

// These endpoints report process readiness only. Dependency checks will be added
// alongside the corresponding infrastructure integration.
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapBranchEndpoints();

app.Run();

public partial class Program
{
}
