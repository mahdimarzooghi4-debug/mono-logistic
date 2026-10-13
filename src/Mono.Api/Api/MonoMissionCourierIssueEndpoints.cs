using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public static class MonoMissionCourierIssueEndpoints
{
    public static IEndpointRouteBuilder MapMonoMissionCourierIssueEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/courier/v1/missions/{missionId}/issues", async (
            string missionId, MissionCourierIssueInput input, HttpRequest request,
            IMonoCourierContext courier, PostgresMissionCourierIssueService issues,
            CancellationToken cancellationToken) =>
        {
            if (!TryReadKey(request, out var key))
                return Results.BadRequest(Error("INVALID_REQUEST", "Exactly one valid Idempotency-Key header is required."));
            try
            {
                return Map(await issues.ReportAsync(courier.CourierId, missionId,
                    input.IssueCode ?? string.Empty, key, cancellationToken));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(Error("INVALID_REQUEST", "Mission ID, issue code, or idempotency key is invalid."));
            }
        }).RequireAuthorization(MonoCourierAuthorizationPolicies.Courier);

        endpoints.MapGet("/operations/v1/missions/{missionId}/courier-issues", async (
            string missionId, PostgresMissionCourierIssueService issues,
            PostgresMissionOperationsReadAuditService readAudit, CancellationToken cancellationToken) =>
        {
            try
            {
                var reports = await issues.ListForMissionAsync(missionId, cancellationToken);
                await readAudit.RecordAsync(PostgresMissionOperationsReadAuditService.MissionCourierIssueSearch,
                    missionId, reports.Count, cancellationToken);
                return Results.Ok(new { reports });
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(Error("INVALID_REQUEST", "Mission ID is invalid."));
            }
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        endpoints.MapPost("/operations/v1/missions/{missionId}/courier-issues/{reportId}/resolve", async (
            string missionId, string reportId, HttpRequest request,
            PostgresMissionCourierIssueService issues, CancellationToken cancellationToken) =>
        {
            if (!TryReadKey(request, out var key))
                return Results.BadRequest(Error("INVALID_REQUEST", "Exactly one valid Idempotency-Key header is required."));
            try
            {
                return Map(await issues.ResolveAsync(missionId, reportId, key, cancellationToken));
            }
            catch (ArgumentException)
            {
                return Results.BadRequest(Error("INVALID_REQUEST", "Mission ID, report ID, or idempotency key is invalid."));
            }
        }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);

        return endpoints;
    }

    private static IResult Map(MissionCourierIssueResult result) => result.Outcome switch
    {
        MissionCourierIssueOutcome.Applied or MissionCourierIssueOutcome.Replayed when result.Report is not null =>
            Results.Ok(new { report = result.Report }),
        MissionCourierIssueOutcome.Conflict => Results.Conflict(Error("IDEMPOTENCY_CONFLICT", "The key was used for a different issue action.")),
        MissionCourierIssueOutcome.MissionNotFound => Results.NotFound(Error("MISSION_NOT_FOUND", "The Mission was not found.")),
        MissionCourierIssueOutcome.IssueNotFound => Results.NotFound(Error("MISSION_ISSUE_NOT_FOUND", "The issue report was not found.")),
        MissionCourierIssueOutcome.InvalidState => Results.Conflict(Error("MISSION_ISSUE_NOT_ALLOWED", "The Mission or issue report is not in a state that permits this action.")),
        _ => throw new InvalidOperationException("Unexpected Mission courier issue outcome.")
    };

    private static bool TryReadKey(HttpRequest request, out string key)
    {
        key = string.Empty;
        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) ||
            values.Count != 1 || string.IsNullOrWhiteSpace(values[0])) return false;
        key = values[0]!.Trim();
        return DeliveryRequestInputValidator.IsValidIdempotencyKey(key);
    }

    private static object Error(string code, string message) => new { error = new { code, message } };
}
