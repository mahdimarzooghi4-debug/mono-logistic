using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;
using Mono.Api.Services;

namespace Mono.Api.Api;

/// <summary>Operations API for initializing and inspecting immutable Route plan versions.</summary>
public static class MonoRoutePlanOperationsEndpoints
{
    public static IEndpointRouteBuilder MapMonoRoutePlanOperationsEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        const string route = "/operations/v1/routes/{routeId}/plan";

        endpoints.MapPost(route, async (
                string routeId,
                InitializeRoutePlanRequest request,
                InitialRoutePlanOrchestrationService initializer,
                CancellationToken cancellationToken) =>
            {
                if (!IsRouteIdValid(routeId) || request is null ||
                    string.IsNullOrWhiteSpace(request.CommandId) || request.CommandId != request.CommandId.Trim() ||
                    request.CommandId.Length > 128 || string.IsNullOrWhiteSpace(request.StartLocationId) ||
                    request.StartedAt == default || request.StartedAt.Offset != TimeSpan.Zero ||
                    string.IsNullOrWhiteSpace(request.MissionId) || request.PickupServiceSeconds is < 0 or > 180 ||
                    request.DropoffServiceSeconds < 0 || request.Evidence is null)
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Route plan initialization request is invalid.")));

                try
                {
                    var result = await initializer.InitializeAsync(new InitialRoutePlanWriteCommand(
                        request.CommandId, routeId, request.StartLocationId, request.StartedAt,
                        request.MissionId, request.PickupServiceSeconds, request.DropoffServiceSeconds,
                        request.Evidence), cancellationToken);
                    IResult response = result.Outcome switch
                    {
                        InitialRoutePlanWriteOutcome.Persisted => Results.Created(
                            $"/operations/v1/routes/{Uri.EscapeDataString(routeId)}/plan/versions/1", result),
                        InitialRoutePlanWriteOutcome.Replayed => Results.Ok(result),
                        InitialRoutePlanWriteOutcome.MissionUnavailable => Results.NotFound(new ApiErrorResponse(
                            new ApiError("CONFIRMED_MISSION_NOT_FOUND", "A confirmed Mission and Promise were not found."))),
                        InitialRoutePlanWriteOutcome.Conflict => Results.Conflict(new ApiErrorResponse(
                            new ApiError(result.ReasonCode ?? "ROUTE_VERSION_CONFLICT", "The Route plan conflicts with the current Route state."))),
                        InitialRoutePlanWriteOutcome.Infeasible => Results.UnprocessableEntity(result),
                        _ => Results.UnprocessableEntity(result)
                    };
                    return response;
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Route plan initialization request is invalid.")));
                }
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet(route, async (
                string routeId,
                IRoutePlanVersionStore routePlans,
                CancellationToken cancellationToken) =>
            {
                if (!IsRouteIdValid(routeId))
                    return (IResult)Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Route ID is invalid.")));

                var current = await routePlans.FindCurrentAsync(routeId, cancellationToken);
                IResult response = current is null
                    ? Results.NotFound(new ApiErrorResponse(new ApiError("ROUTE_NOT_FOUND", "The Route plan was not found.")))
                    : Results.Ok(current);
                return response;
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapGet(route + "/versions/{version:int}", async (
                string routeId,
                int version,
                IRoutePlanVersionStore routePlans,
                CancellationToken cancellationToken) =>
            {
                if (!IsRouteIdValid(routeId) || version <= 0)
                    return (IResult)Results.BadRequest(new ApiErrorResponse(
                        new ApiError("INVALID_REQUEST", "Route ID or version is invalid.")));

                var plan = await routePlans.FindVersionAsync(routeId, version, cancellationToken);
                IResult response = plan is null
                    ? Results.NotFound(new ApiErrorResponse(new ApiError("ROUTE_PLAN_VERSION_NOT_FOUND", "The Route plan version was not found.")))
                    : Results.Ok(plan);
                return response;
            })
            .RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }

    private static bool IsRouteIdValid(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && value == value.Trim() &&
        !value.Any(char.IsControl);
}

public sealed record InitializeRoutePlanRequest(
    string CommandId,
    string StartLocationId,
    DateTimeOffset StartedAt,
    string MissionId,
    int PickupServiceSeconds,
    int DropoffServiceSeconds,
    RouteTravelTimeEvidence Evidence);
