using System.Text.Json.Serialization;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record CourierWithdrawalHistoryEventResponse(
    [property: JsonPropertyName("event_sequence")] long EventSequence,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("actor_type")] string ActorType,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("reason_code")] string? ReasonCode,
    [property: JsonPropertyName("bank_reference")] string? BankReference,
    [property: JsonPropertyName("idempotency_key")] string IdempotencyKey);

public sealed record CourierWithdrawalHistoryResponse(
    [property: JsonPropertyName("withdrawal_id")] string WithdrawalId,
    [property: JsonPropertyName("events")] IReadOnlyList<CourierWithdrawalHistoryEventResponse> Events);

public sealed record CourierWithdrawalTimelineEventResponse(
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("occurred_at")] DateTimeOffset OccurredAt,
    [property: JsonPropertyName("reason_code")] string? ReasonCode,
    [property: JsonPropertyName("bank_reference")] string? BankReference);

public sealed record CourierWithdrawalTimelineResponse(
    [property: JsonPropertyName("withdrawal_id")] string WithdrawalId,
    [property: JsonPropertyName("events")] IReadOnlyList<CourierWithdrawalTimelineEventResponse> Events);

public static class MonoCourierWithdrawalHistoryEndpoints
{
    public static IEndpointRouteBuilder MapMonoCourierWithdrawalHistoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/courier/v1/finance/withdrawals/{withdrawalId}/history",
                async Task<IResult> (string withdrawalId, HttpContext httpContext,
                    IMonoCourierContext courier, PostgresCourierWithdrawalHistoryReadService service,
                    CancellationToken cancellationToken) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var result = await service.ReadForCourierAsync(
                        courier.CourierId, withdrawalId, cancellationToken);
                    if (result.OutcomeCode != "FOUND")
                        return Results.NotFound(new { error_code = "WITHDRAWAL_NOT_FOUND" });
                    var events = result.Events.Select(item => new CourierWithdrawalTimelineEventResponse(
                        item.Action, item.OccurredAt, item.ReasonCode, item.BankReference)).ToArray();
                    return Results.Ok(new CourierWithdrawalTimelineResponse(result.WithdrawalId, events));
                })
            .RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapGet("/operations/v1/courier-withdrawals/{withdrawalId}/history",
                async Task<IResult> (string withdrawalId, HttpContext httpContext,
                    PostgresCourierWithdrawalHistoryReadService service, TimeProvider timeProvider,
                    CancellationToken cancellationToken) =>
                {
                    httpContext.Response.Headers.CacheControl = "no-store";
                    var result = await service.ReadAsync(withdrawalId, timeProvider.GetUtcNow(), cancellationToken);
                    if (result.OutcomeCode != "FOUND")
                        return Results.NotFound(new { error_code = "WITHDRAWAL_NOT_FOUND" });
                    var events = result.Events.Select(item => new CourierWithdrawalHistoryEventResponse(
                        item.EventSequence, item.Action, item.ActorType, item.ActorId, item.OccurredAt,
                        item.ReasonCode, item.BankReference, item.IdempotencyKey)).ToArray();
                    return Results.Ok(new CourierWithdrawalHistoryResponse(result.WithdrawalId, events));
                })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }
}
