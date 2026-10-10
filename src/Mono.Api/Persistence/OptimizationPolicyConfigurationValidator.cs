using System.Text.Json;

namespace Mono.Api.Persistence;

/// <summary>Validates the v1 configuration inputs for optimization policies.</summary>
public static class OptimizationPolicyConfigurationValidator
{
    private static readonly HashSet<string> Criteria = new(StringComparer.Ordinal)
    {
        "eta", "detour", "route_density", "pickup_fit", "capacity_balance", "cost"
    };

    public static void Validate(JsonElement definition)
    {
        if (definition.ValueKind != JsonValueKind.Object ||
            definition.EnumerateObject().Count() != 2 ||
            !definition.TryGetProperty("scoringModel", out var scoringModel) ||
            scoringModel.ValueKind != JsonValueKind.String ||
            !string.Equals(scoringModel.GetString(), "WEIGHTED_SUM", StringComparison.Ordinal) ||
            !definition.TryGetProperty("criteria", out var criteria) ||
            criteria.ValueKind != JsonValueKind.Array ||
            criteria.GetArrayLength() is < 1 or > 6)
        {
            throw new ArgumentException("Optimization definition must contain a WEIGHTED_SUM model and 1–6 criteria.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in criteria.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                item.EnumerateObject().Count() != 3 ||
                !item.TryGetProperty("name", out var nameElement) ||
                nameElement.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("weight", out var weightElement) ||
                weightElement.ValueKind != JsonValueKind.Number ||
                !weightElement.TryGetDecimal(out var weight) || weight < 0 ||
                !item.TryGetProperty("priority", out var priorityElement) ||
                priorityElement.ValueKind != JsonValueKind.Number ||
                !priorityElement.TryGetInt32(out var priority) || priority < 1)
            {
                throw new ArgumentException("Each optimization criterion needs a supported name, non-negative decimal weight, and positive integer priority.");
            }

            var name = nameElement.GetString() ?? string.Empty;
            if (!Criteria.Contains(name) || !seen.Add(name))
                throw new ArgumentException("Optimization criteria must use unique names from the supported v1 catalog.");
        }
    }
}
