using Mono.Api.Api;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();
builder.Services.AddMonoCustomerTenantAuthorization();
builder.Services.AddMonoApiKeyAuthentication(builder.Configuration);
builder.Services.AddMonoOperationsAuthentication(builder.Configuration);
builder.Services.AddScoped<CustomerServiceConfigurationAuthoringService>();
builder.Services.AddScoped<CustomerServiceConfigurationInspectionService>();
builder.Services.AddScoped<MonoCustomerOperationsService>();
builder.Services.AddScoped<MonoCustomerExternalIdentifierOperationsService>();
builder.Services.AddScoped<MonoApiKeyOperationsService>();
builder.Services.AddScoped<MonoBranchOperationsService>();
builder.Services.AddScoped<MonoBranchInspectionService>();
builder.Services.AddScoped<MonoBranchExternalIdentifierOperationsService>();
builder.Services.AddScoped<MonoPolicyDraftOperationsService>();
builder.Services.AddScoped<MonoEffectivePolicyResolutionService>();
builder.Services.AddScoped<MonoHardPolicyEvaluationService>();
builder.Services.AddScoped<CustomerBranchRepository>();
builder.Services.AddScoped<CustomerServiceConfigurationRepository>();
builder.Services.AddScoped<PostgresDeliveryRequestRepository>();
builder.Services.AddScoped<PostgresMissionConfirmationRepository>();
builder.Services.AddScoped<PostgresMissionLifecycleRepository>();
builder.Services.AddScoped<PostgresMissionHistoryRepository>();
builder.Services.AddScoped<PostgresMissionOperationsService>();
builder.Services.AddScoped<PostgresMissionOperationsReadAuditService>();
builder.Services.AddScoped<PostgresDeliveryRequestOperationsService>();
builder.Services.AddScoped<PostgresDeliveryRequestOperationsReadAuditService>();
builder.Services.AddScoped<PostgresPromiseCommitRepository>();
builder.Services.AddScoped<PostgresDeliveryQuoteRepository>();
builder.Services.AddScoped<PostgresZoneGeometryRepository>();
builder.Services.AddScoped<PostgresZoneGeometryReviewService>();
builder.Services.AddScoped<PostgresZoneGeometryActivationService>();
builder.Services.AddScoped<MonoZoneCoveragePreviewService>();
builder.Services.AddScoped<MonoZoneGeometryCatalogService>();
builder.Services.AddScoped<MonoZoneGeometryHistoryService>();
builder.Services.AddScoped<MonoZoneGeometryQualityService>();
builder.Services.AddScoped<MonoZoneCoverageCompletenessService>();
builder.Services.AddScoped<MonoZoneGeometryGeoJsonService>();
builder.Services.AddScoped<MonoActiveZoneGeoJsonLayerService>();
builder.Services.AddScoped<IDeliveryQuoteStore>(services =>
    services.GetRequiredService<PostgresDeliveryQuoteRepository>());
builder.Services.AddScoped<DeliveryQuoteIssuanceApplicationService>();
builder.Services.AddScoped<IPromiseCommitStore>(services =>
    services.GetRequiredService<PostgresPromiseCommitRepository>());
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ConfiguredEvidenceSourceVerifier>();
builder.Services.AddScoped<IFeasibilityEvidenceSourceVerifier>(services =>
    services.GetRequiredService<ConfiguredEvidenceSourceVerifier>());
builder.Services.AddScoped<IHanaDistanceEvidenceSourceVerifier>(services =>
    services.GetRequiredService<ConfiguredEvidenceSourceVerifier>());
builder.Services.AddScoped<PromiseCommitApplicationService>();

var app = builder.Build();

// Customer authentication is invoked by its explicit authorization policy.
// Health endpoints therefore remain independent of credential storage.
app.UseAuthentication();
app.UseAuthorization();

// These endpoints report process readiness only. Dependency checks will be added
// alongside the corresponding infrastructure integration.
app.MapHealthChecks("/health/live");
app.MapHealthChecks("/health/ready");
app.MapBranchEndpoints();
app.MapDeliveryRequestEndpoints();
app.MapMissionConfirmationEndpoints();
app.MapMissionLifecycleEndpoints();
app.MapMissionHistoryEndpoints();
app.MapMonoMissionOperationsEndpoints();
app.MapCustomerServiceConfigurationInspectionEndpoints();
app.MapCustomerServiceConfigurationAuthoringEndpoints();
app.MapMonoCustomerOperationsEndpoints();
app.MapMonoApiKeyOperationsEndpoints();
app.MapMonoBranchOperationsEndpoints();
app.MapMonoBranchExternalIdentifierOperationsEndpoints();
app.MapMonoPolicyDraftOperationsEndpoints();
app.MapMonoHardPolicyEvaluationEndpoints();
app.MapMonoZoneGeometryReviewEndpoints();
app.MapMonoZoneGeometryActivationEndpoints();
app.MapMonoZoneCoveragePreviewEndpoints();
app.MapMonoZoneGeometryCatalogEndpoints();
app.MapMonoZoneGeometryHistoryEndpoints();
app.MapMonoZoneGeometryQualityEndpoints();
app.MapMonoZoneContainmentEndpoints();
app.MapMonoZoneCoverageCompletenessEndpoints();
app.MapMonoZoneGeometryGeoJsonEndpoints();
app.MapMonoActiveZoneGeoJsonLayerEndpoints();

app.Run();

public partial class Program
{
}
