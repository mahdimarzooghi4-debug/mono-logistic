using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public sealed record MissionHistoryPageResponse(
    [property: JsonPropertyName("items")] IReadOnlyList<MissionConfirmationResponse> Items,
    [property: JsonPropertyName("next_cursor")] string? NextCursor);

public static class MissionHistoryInputValidator
{
    private static readonly Regex MissionIdPattern = new(
        @"\Amis_[0-9a-f]{32}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsValidMissionId(string? value) =>
        !string.IsNullOrEmpty(value) && MissionIdPattern.IsMatch(value);
}
