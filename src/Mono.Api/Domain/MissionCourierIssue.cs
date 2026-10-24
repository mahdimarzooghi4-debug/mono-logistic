using System.Text.Json.Serialization;

namespace Mono.Api.Domain;

public sealed record MissionCourierIssueInput(
    [property: JsonPropertyName("issue_code")] string? IssueCode);

public sealed record MissionCourierIssueReport(
    [property: JsonPropertyName("report_id")] string ReportId,
    [property: JsonPropertyName("mission_id")] string MissionId,
    [property: JsonPropertyName("courier_id")] string CourierId,
    [property: JsonPropertyName("issue_code")] string IssueCode,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("reported_at")] DateTimeOffset ReportedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt);

public enum MissionCourierIssueOutcome
{
    Applied,
    Replayed,
    Conflict,
    MissionNotFound,
    IssueNotFound,
    InvalidState
}

public sealed record MissionCourierIssueResult(
    MissionCourierIssueOutcome Outcome,
    MissionCourierIssueReport? Report);
