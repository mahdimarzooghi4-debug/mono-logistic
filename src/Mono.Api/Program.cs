var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

// These endpoints report process readiness only. Dependency checks will be added
// alongside the corresponding infrastructure integration.
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");

app.Run();

public partial class Program
{
}
