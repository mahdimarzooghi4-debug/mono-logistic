using Mono.Api.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddMonoCustomerTenantAuthorization();
builder.Services.AddMonoApiKeyAuthentication(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// These endpoints report process readiness only. Dependency checks will be added
// alongside the corresponding infrastructure integration.
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.Run();

public partial class Program
{
}
