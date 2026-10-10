using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mono.Api.Persistence;

/// <summary>Evaluates the intentionally small v1 declarative subset for HARD policies.</summary>
public sealed class MonoHardPolicyEvaluationService(MonoEffectivePolicyResolutionService resolver)
{
    public async Task<HardPolicyEvaluationResponse> EvaluateAsync(
        EffectivePolicyContext context,
        JsonElement facts,
        CancellationToken cancellationToken = default)
    {
        var resolution = await resolver.ResolveAsync(context, cancellationToken);
        return HardPolicyEvaluator.Evaluate(resolution, facts);
    }

    public async Task<EligibleModeEvaluationResponse> EvaluateEligibleModesAsync(
        EffectivePolicyContext context,
        JsonElement facts,
        CancellationToken cancellationToken = default)
    {
        var resolution = await resolver.ResolveAsync(context, cancellationToken);
        return EligibleModeEvaluator.Evaluate(resolution, facts);
    }
}

public static class HardPolicyEvaluator
{
    private static readonly Regex FactNamePattern = new(
        @"\A[a-z][a-z0-9_]{0,63}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReasonCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<string> Operators = new(StringComparer.Ordinal)
    {
        "EQ", "NE", "IN", "NOT_IN", "GT", "GTE", "LT", "LTE"
    };

    public static HardPolicyEvaluationResponse Evaluate(
        EffectivePolicyResolutionResponse resolution,
        JsonElement facts)
    {
        if (facts.ValueKind != JsonValueKind.Object || facts.EnumerateObject().Count() > 100)
            throw new ArgumentException("Facts must be an object with at most 100 top-level fields.", nameof(facts));

        var results = new List<HardPolicyResult>();
        foreach (var policy in resolution.Policies.Where(
                     item => string.Equals(item.EnforcementType, "HARD", StringComparison.Ordinal)))
        {
            results.Add(EvaluatePolicy(policy, facts));
        }

        var reasonCodes = results.SelectMany(result => result.ReasonCodes)
            .Distinct(StringComparer.Ordinal).ToArray();
        return new HardPolicyEvaluationResponse(
            resolution.RequestedAt,
            results.Count,
            resolution.Policies.Count - results.Count,
            results.All(result => result.Passed),
            results.Any(result => result.CandidateExcluded),
            reasonCodes,
            results);
    }

    private static HardPolicyResult EvaluatePolicy(ResolvedPolicyVersion policy, JsonElement facts)
    {
        if (policy.Definition.ValueKind != JsonValueKind.Object ||
            !policy.Definition.TryGetProperty("constraints", out var constraints) ||
            constraints.ValueKind != JsonValueKind.Array ||
            constraints.GetArrayLength() is < 1 or > 100)
        {
            return Failed(policy, "POLICY_DEFINITION_UNSUPPORTED");
        }

        var reasons = new List<string>();
        foreach (var constraint in constraints.EnumerateArray())
        {
            if (!TryReadConstraint(constraint, out var fact, out var op, out var expected, out var reasonCode))
                return Failed(policy, "POLICY_DEFINITION_UNSUPPORTED");

            if (!facts.TryGetProperty(fact, out var actual))
            {
                reasons.Add("POLICY_FACT_MISSING");
                continue;
            }

            if (!TryCompare(actual, op, expected, out var matches))
                return Failed(policy, "POLICY_DEFINITION_UNSUPPORTED");

            if (!matches)
                reasons.Add(reasonCode);
        }

        var distinctReasons = reasons.Distinct(StringComparer.Ordinal).ToArray();
        return new HardPolicyResult(
            policy.PolicyId, policy.VersionNumber, distinctReasons.Length == 0,
            distinctReasons.Length > 0, distinctReasons);
    }

    private static bool TryReadConstraint(
        JsonElement constraint, out string fact, out string op, out JsonElement expected, out string reasonCode)
    {
        fact = op = reasonCode = string.Empty;
        expected = default;
        if (constraint.ValueKind != JsonValueKind.Object ||
            constraint.EnumerateObject().Count() != 4 ||
            !constraint.TryGetProperty("fact", out var factElement) ||
            factElement.ValueKind != JsonValueKind.String ||
            !constraint.TryGetProperty("operator", out var opElement) ||
            opElement.ValueKind != JsonValueKind.String ||
            !constraint.TryGetProperty("value", out expected) ||
            !constraint.TryGetProperty("reasonCode", out var reasonElement) ||
            reasonElement.ValueKind != JsonValueKind.String)
            return false;

        fact = factElement.GetString() ?? string.Empty;
        op = (opElement.GetString() ?? string.Empty).ToUpperInvariant();
        reasonCode = reasonElement.GetString() ?? string.Empty;
        if (!FactNamePattern.IsMatch(fact) || !Operators.Contains(op) ||
            !ReasonCodePattern.IsMatch(reasonCode) || expected.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return false;

        if (op is "IN" or "NOT_IN")
            return expected.ValueKind == JsonValueKind.Array &&
                expected.GetArrayLength() is > 0 and <= 100 &&
                expected.EnumerateArray().All(IsScalar);
        return IsScalar(expected);
    }

    private static bool TryCompare(JsonElement actual, string op, JsonElement expected, out bool matches)
    {
        matches = false;
        if (!IsScalar(actual))
            return false;

        if (op is "IN" or "NOT_IN")
        {
            var contains = expected.EnumerateArray().Any(item => ScalarEquals(actual, item));
            matches = op == "IN" ? contains : !contains;
            return true;
        }

        if (op is "EQ" or "NE")
        {
            matches = ScalarEquals(actual, expected);
            if (op == "NE") matches = !matches;
            return true;
        }

        if (actual.ValueKind == JsonValueKind.Number && expected.ValueKind == JsonValueKind.Number)
        {
            if (!actual.TryGetDecimal(out var left) || !expected.TryGetDecimal(out var right))
                return false;
            matches = ApplyComparison(left.CompareTo(right), op);
            return true;
        }

        if (actual.ValueKind == JsonValueKind.String && expected.ValueKind == JsonValueKind.String)
        {
            var comparison = string.Compare(actual.GetString(), expected.GetString(), StringComparison.Ordinal);
            matches = ApplyComparison(comparison, op);
            return true;
        }

        return false;
    }

    private static bool ApplyComparison(int comparison, string op) => op switch
    {
        "GT" => comparison > 0,
        "GTE" => comparison >= 0,
        "LT" => comparison < 0,
        "LTE" => comparison <= 0,
        _ => false
    };

    private static bool ScalarEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;
        return left.ValueKind switch
        {
            JsonValueKind.String => string.Equals(left.GetString(), right.GetString(), StringComparison.Ordinal),
            JsonValueKind.Number => left.TryGetDecimal(out var l) && right.TryGetDecimal(out var r) && l == r,
            JsonValueKind.True => true,
            JsonValueKind.False => true,
            _ => false
        };
    }

    private static bool IsScalar(JsonElement value) =>
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number or
            JsonValueKind.True or JsonValueKind.False;

    private static HardPolicyResult Failed(ResolvedPolicyVersion policy, string reasonCode) =>
        new(policy.PolicyId, policy.VersionNumber, false, true, [reasonCode]);
}

public static class EligibleModeEvaluator
{
    public static EligibleModeEvaluationResponse Evaluate(
        EffectivePolicyResolutionResponse resolution, JsonElement facts)
    {
        if (facts.ValueKind != JsonValueKind.Object || facts.EnumerateObject().Count() > 100)
            throw new ArgumentException("Facts must be an object with at most 100 top-level fields.", nameof(facts));

        var capabilityPolicies = resolution.Policies.Where(policy =>
            policy.EnforcementType == "HARD" && policy.PolicyType == "MODE_CAPABILITY").ToArray();

        if (capabilityPolicies.Length == 0)
            return new EligibleModeEvaluationResponse(
                resolution.RequestedAt, [], [], ["MODE_CAPABILITY_POLICY_MISSING"]);

        var candidateModes = new List<string>();
        var seenModes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var policy in capabilityPolicies)
        {
            try
            {
                ModeCapabilityPolicyConfigurationValidator.Validate(policy.Definition);
            }
            catch (ArgumentException)
            {
                return new EligibleModeEvaluationResponse(
                    resolution.RequestedAt, [], [], ["POLICY_DEFINITION_UNSUPPORTED"]);
            }

            foreach (var mode in policy.Definition.GetProperty("modes").EnumerateArray())
            {
                var code = mode.GetString()!;
                if (seenModes.Add(code))
                    candidateModes.Add(code);
            }
        }

        var candidates = new List<EligibleModeCandidateResult>();
        foreach (var mode in candidateModes)
        {
            var modeFacts = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var fact in facts.EnumerateObject())
                modeFacts[fact.Name] = fact.Value.Clone();
            modeFacts["mode"] = JsonSerializer.SerializeToElement(mode);

            var evaluation = HardPolicyEvaluator.Evaluate(
                resolution, JsonSerializer.SerializeToElement(modeFacts));
            candidates.Add(new EligibleModeCandidateResult(
                mode, evaluation.Passed, evaluation.ReasonCodes));
        }

        var eligibleModes = candidates.Where(candidate => candidate.Eligible)
            .Select(candidate => candidate.Mode).ToArray();
        var reasonCodes = candidates.SelectMany(candidate => candidate.ReasonCodes)
            .Distinct(StringComparer.Ordinal).ToArray();
        return new EligibleModeEvaluationResponse(
            resolution.RequestedAt, eligibleModes, candidates, reasonCodes);
    }
}

public sealed record EligibleModeEvaluationResponse(
    DateTimeOffset RequestedAt,
    IReadOnlyList<string> EligibleModes,
    IReadOnlyList<EligibleModeCandidateResult> Candidates,
    IReadOnlyList<string> ReasonCodes);

public sealed record EligibleModeCandidateResult(
    string Mode,
    bool Eligible,
    IReadOnlyList<string> ReasonCodes);

public sealed record HardPolicyEvaluationResponse(
    DateTimeOffset RequestedAt,
    int EvaluatedHardPolicyCount,
    int IgnoredNonHardPolicyCount,
    bool Passed,
    bool CandidateExcluded,
    IReadOnlyList<string> ReasonCodes,
    IReadOnlyList<HardPolicyResult> Policies);

public sealed record HardPolicyResult(
    string PolicyId,
    int VersionNumber,
    bool Passed,
    bool CandidateExcluded,
    IReadOnlyList<string> ReasonCodes);
