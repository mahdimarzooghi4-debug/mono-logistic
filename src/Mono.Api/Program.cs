using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Npgsql;
using Mono.Api.Api;
using Mono.Api.Domain;
using Mono.Api.Health;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks()
    .AddCheck<MonoPostgresHealthCheck>("postgres", tags: ["ready"]);
builder.Services.AddMonoCustomerTenantAuthorization();
builder.Services.AddMonoApiKeyAuthentication(builder.Configuration);
builder.Services.AddMonoOperationsAuthentication(builder.Configuration);
builder.Services.AddMonoCourierAuthentication(builder.Configuration);
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
builder.Services.AddScoped<PostgresCourierEarningLedger>();
builder.Services.AddScoped<PostgresCourierWithdrawalService>();
builder.Services.AddScoped<PostgresCourierWithdrawalHistoryReadService>();
builder.Services.AddScoped<PostgresCourierWithdrawalOperationsSearchService>();
builder.Services.AddScoped<PostgresCourierWithdrawalOperationsAuditReadService>();
builder.Services.AddScoped<PostgresCourierPayoutDestinationAuditReadService>();
builder.Services.AddSingleton(_ => new CourierPayoutIbanProtector(builder.Configuration["MonoCourier:PayoutIbanEncryptionKey"]));
builder.Services.AddScoped<PostgresCourierOnboardingService>();
builder.Services.AddScoped<PostgresWebhookEndpointRepository>();
builder.Services.AddSingleton(_ => new WebhookSecretProtector(builder.Configuration["Mono:WebhookEncryptionKey"]));
builder.Services.AddSingleton<IWebhookDnsResolver, SystemWebhookDnsResolver>();
builder.Services.AddSingleton(services => WebhookDeliveryHttpClientFactory.Create(
    services.GetRequiredService<IWebhookDnsResolver>()));
builder.Services.AddSingleton<IWebhookDeliveryTransport, WebhookHttpDeliveryTransport>();
builder.Services.AddScoped<PostgresWebhookDeliveryRepository>();
builder.Services.AddScoped<PostgresWebhookDeliveryHistoryRepository>();
builder.Services.AddScoped<PostgresWebhookOperationsReadService>();
builder.Services.AddScoped<WebhookDeliveryDispatcher>();
builder.Services.AddHostedService<WebhookDeliveryBackgroundService>();
builder.Services.AddScoped<PostgresCourierOperationsReadAuditService>();
builder.Services.AddScoped<PostgresCourierAvailabilityService>();
builder.Services.AddScoped<PostgresCourierCapacityProjectionService>();
builder.Services.AddScoped<PostgresCourierSuspensionService>();
builder.Services.AddScoped<PostgresCourierLivePresenceService>();
builder.Services.AddScoped<PostgresMissionCourierOfferService>();
builder.Services.AddScoped<PostgresMissionCourierIssueService>();
builder.Services.AddScoped(services => new PostgresCourierIdentityBindingService(
    services.GetRequiredService<NpgsqlDataSource>(),
    services.GetRequiredService<IMonoOperationsContext>(),
    builder.Configuration["MonoCourier:Authority"],
    services.GetRequiredService<TimeProvider>()));
builder.Services.AddScoped<PostgresCourierPlanningEligibilityService>();
builder.Services.AddScoped<PostgresMissionHistoryRepository>();
builder.Services.AddScoped<PostgresMissionOperationsService>();
builder.Services.AddScoped<PostgresMissionDispatchQueueService>();
builder.Services.AddScoped<PostgresMissionDispatchAttemptHistoryService>();
builder.Services.AddScoped<PostgresMissionOperationsReadAuditService>();
builder.Services.AddScoped<PostgresDeliveryRequestOperationsService>();
builder.Services.AddScoped<PostgresDeliveryRequestOperationsReadAuditService>();
builder.Services.AddScoped<PostgresPromiseCommitRepository>();
builder.Services.AddScoped<PostgresDeliveryQuoteRepository>();
builder.Services.AddScoped<PostgresZoneGeometryRepository>();
builder.Services.AddScoped<PostgresRoutePlanVersionRepository>();
builder.Services.AddScoped<IRoutePlanVersionStore>(services =>
    services.GetRequiredService<PostgresRoutePlanVersionRepository>());
builder.Services.AddScoped<PostgresRouteLocationCoordinateCatalog>();
builder.Services.AddScoped<IInitialRouteMissionCandidateSource, PostgresInitialRouteMissionCandidateSource>();
builder.Services.AddScoped<IRouteLocationCoordinateCatalog>(services =>
    services.GetRequiredService<PostgresRouteLocationCoordinateCatalog>());
builder.Services.AddScoped<IRouteLocationCoordinateResolver>(services =>
    services.GetRequiredService<PostgresRouteLocationCoordinateCatalog>());
builder.Services.AddScoped<DeliveryRequestRouteLocationRegistrar>();
builder.Services.AddScoped<IMapsProvider, UnconfiguredMapsProvider>();
builder.Services.AddScoped<IMapsGateway, MapsGateway>();
builder.Services.AddScoped<IRouteTravelTimeEvidenceVerifier, ConfiguredRouteTravelTimeEvidenceVerifier>();
builder.Services.AddScoped<IInitialRoutePlanEvidenceVerifier, ConfiguredInitialRoutePlanEvidenceVerifier>();
builder.Services.AddScoped<InitialRouteMatrixAcquisitionService>();
builder.Services.AddScoped<InitialRoutePlanOrchestrationService>();
builder.Services.AddScoped<RoutePlanInsertionOrchestrationService>();
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

// Liveness is process-only; readiness includes registered infrastructure dependencies.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapBranchEndpoints();
app.MapDeliveryRequestEndpoints();
app.MapWebhookEndpointEndpoints();
app.MapWebhookDeliveryHistoryEndpoints();
app.MapWebhookOperationsEndpoints();
app.MapMissionConfirmationEndpoints();
app.MapMonoMissionLifecycleEndpoints();
app.MapMissionCancellationEndpoints();
app.MapMissionHistoryEndpoints();
app.MapMonoMissionOperationsEndpoints();
app.MapMonoMissionDispatchAttemptHistoryEndpoints();
app.MapMonoRoutePlanOperationsEndpoints();
app.MapMonoCourierOperationsEndpoints();
app.MapMonoCourierSuspensionEndpoints();
app.MapMonoCourierSessionEndpoints();
app.MapMonoCourierFinanceEndpoints();
app.MapMonoCourierWithdrawalEndpoints();
app.MapMonoCourierWithdrawalHistoryEndpoints();
app.MapMonoCourierWithdrawalOperationsSearchEndpoints();
app.MapMonoCourierPayoutDestinationAuditEndpoints();
app.MapMonoCourierIdentityBindingEndpoints();
app.MapMonoCourierLivePresenceEndpoints();
app.MapMonoMissionCourierOfferEndpoints();
app.MapMonoMissionCourierIssueEndpoints();
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
