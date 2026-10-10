using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mono.Api.Persistence;

/// <summary>Validates the mode identifiers recorded by a MODE_CAPABILITY policy.</summary>
public static class ModeCapabilityPolicyConfigurationValidator
{
    private static readonly Regex ModeCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{0,31}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void Validate(JsonElement definition)
    {
        if (definition.ValueKind != JsonValueKind.Object ||
            definition.EnumerateObject().Count() != 1 ||
            !definition.TryGetProperty("modes", out var modes) ||
            modes.ValueKind != JsonValueKind.Array ||
            modes.GetArrayLength() is < 1 or > 32)
        {
            throw new ArgumentException("Mode capability definition must contain a non-empty modes array.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in modes.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new ArgumentException("Each mode must be a canonical uppercase identifier.");

            var code = item.GetString() ?? string.Empty;
            if (!ModeCodePattern.IsMatch(code) || !seen.Add(code))
                throw new ArgumentException("Mode identifiers must be unique canonical uppercase identifiers.");
        }
    }
}
