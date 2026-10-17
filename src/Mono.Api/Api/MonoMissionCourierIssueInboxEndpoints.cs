using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Api;

public sealed record MissionCourierIssueInboxPageResponse(
    IReadOnlyList<Mono.Api.Domain.MissionCourierIssueReport> Reports, string? NextCursor);

public static class MonoMissionCourierIssueInboxEndpoints
{
    private static readonly Regex ReportIdPattern = new(@"\Amci_[0-9a-f]{32}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IEndpointRouteBuilder MapMonoMissionCourierIssueInboxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/operations/v1/courier-issues",
            async Task<IResult> (HttpContext context, PostgresMissionCourierIssueInboxService service,
                [FromQuery(Name = "limit")] int? limit,
                [FromQuery(Name = "status")] string? status,
                [FromQuery(Name = "issue_code")] string? issueCode,
                [FromQuery(Name = "mission_id")] string? missionId,
                [FromQuery(Name = "courier_id")] string? courierId,
                [FromQuery(Name = "from_at")] DateTimeOffset? fromAt,
                [FromQuery(Name = "to_at")] DateTimeOffset? toAt,
                [FromQuery(Name = "cursor")] string? cursor,
                CancellationToken cancellationToken) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                var size = limit ?? 50;
                status = Clean(status)?.ToUpperInvariant();
                issueCode = Clean(issueCode)?.ToUpperInvariant();
                missionId = Clean(missionId);
                courierId = Clean(courierId);
                fromAt = fromAt?.ToUniversalTime();
                toAt = toAt?.ToUniversalTime();
                if (size is < 1 or > 100 ||
                    (status is not (null or "OPEN" or "RESOLVED")) ||
                    (issueCode is not (null or "ACCESS_BLOCKED" or "RECIPIENT_UNAVAILABLE" or "PACKAGE_MISMATCH" or "SAFETY_CONCERN" or "OTHER")) ||
                    (fromAt.HasValue && toAt.HasValue && fromAt >= toAt))
                    return Results.BadRequest(new { error_code = "INVALID_FILTERS" });
                if (!TryDecodeCursor(cursor, status, issueCode, missionId, courierId, fromAt, toAt, out var before))
                    return Results.BadRequest(new { error_code = "INVALID_CURSOR" });
                try
                {
                    var page = await service.SearchAsync(size, status, issueCode, missionId, courierId,
                        fromAt, toAt, before, cancellationToken);
                    var nextCursor = page.NextCursor is null ? null : EncodeCursor(
                        status, issueCode, missionId, courierId, fromAt, toAt, page.NextCursor);
                    return Results.Ok(new MissionCourierIssueInboxPageResponse(page.Reports, nextCursor));
                }
                catch (ArgumentException)
                {
                    return Results.BadRequest(new { error_code = "INVALID_FILTERS" });
                }
            }).RequireAuthorization(MonoOperationsAuthorizationPolicies.Administrator);
        return endpoints;
    }

    private sealed record CursorPayload(string? Status, string? IssueCode, string? MissionId, string? CourierId,
        string? FromAt, string? ToAt, string? ReportedAt, string? ReportId);

    private static string EncodeCursor(string? status, string? issueCode, string? missionId, string? courierId,
        DateTimeOffset? fromAt, DateTimeOffset? toAt, MissionCourierIssueInboxCursor cursor) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new CursorPayload(status,
            issueCode, missionId, courierId, Format(fromAt), Format(toAt), Format(cursor.ReportedAt), cursor.ReportId))))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryDecodeCursor(string? encoded, string? status, string? issueCode, string? missionId,
        string? courierId, DateTimeOffset? fromAt, DateTimeOffset? toAt,
        out MissionCourierIssueInboxCursor? cursor)
    {
        cursor = null;
        if (encoded is null) return true;
        if (encoded.Length is < 8 or > 2048) return false;
        try
        {
            var value = encoded.Replace('-', '+').Replace('_', '/');
            value += new string('=', (4 - value.Length % 4) % 4);
            var payload = JsonSerializer.Deserialize<CursorPayload>(Convert.FromBase64String(value));
            if (payload is null || payload.Status != status || payload.IssueCode != issueCode ||
                payload.MissionId != missionId || payload.CourierId != courierId ||
                payload.FromAt != Format(fromAt) || payload.ToAt != Format(toAt) ||
                payload.ReportId is null || !ReportIdPattern.IsMatch(payload.ReportId) ||
                !DateTimeOffset.TryParse(payload.ReportedAt, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var reportedAt))
                return false;
            cursor = new MissionCourierIssueInboxCursor(reportedAt, payload.ReportId);
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string? Format(DateTimeOffset? value) => value?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
