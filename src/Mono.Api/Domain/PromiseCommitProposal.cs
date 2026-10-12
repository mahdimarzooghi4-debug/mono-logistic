using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public sealed record PromiseCommitProposal(
    string PromiseId,
    string DeliveryRequestId,
    string DecisionId,
    int Version,
    DateTimeOffset EvaluatedAt,
    DateTimeOffset CommittedDeliveryAt,
    FeasibilityOutcome Outcome,
    TimeSpan SafetyBuffer,
    string SafetyBufferPolicyVersion,
    IReadOnlyDictionary<string, string> PolicyVersions,
    IReadOnlyList<string> ReasonCodes);

public static class PromiseCommitProposalValidator
{
    private static readonly Regex PromiseIdPattern = new(
        @"\Aprm_[0-9a-f]{32}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DeliveryRequestIdPattern = new(
        @"\Adrq_[0-9a-f]{32}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReasonCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static void Validate(PromiseCommitProposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        if (!PromiseIdPattern.IsMatch(proposal.PromiseId ?? string.Empty))
            throw new ArgumentException("Promise ID must be a valid Mono promise ID.", nameof(proposal));
        if (!DeliveryRequestIdPattern.IsMatch(proposal.DeliveryRequestId ?? string.Empty))
            throw new ArgumentException("Delivery request ID must be a valid Mono delivery request ID.", nameof(proposal));
        if (string.IsNullOrWhiteSpace(proposal.DecisionId) ||
            proposal.DecisionId.Length > 128 ||
            proposal.DecisionId.Any(char.IsControl))
            throw new ArgumentException("Decision ID must be a non-empty identifier up to 128 characters.", nameof(proposal));
        if (proposal.Version < 1)
            throw new ArgumentException("Promise version must be positive.", nameof(proposal));
        if (proposal.EvaluatedAt == default ||
            proposal.CommittedDeliveryAt == default ||
            proposal.CommittedDeliveryAt <= proposal.EvaluatedAt)
            throw new ArgumentException("Promise timestamps must be valid and delivery must follow evaluation.", nameof(proposal));
        if (!Enum.IsDefined(proposal.Outcome) || proposal.Outcome == FeasibilityOutcome.Rejected)
            throw new ArgumentException("Only accepted or alternative promises may be proposed for commitment.", nameof(proposal));
        if (proposal.SafetyBuffer < TimeSpan.Zero)
            throw new ArgumentException("Safety buffer cannot be negative.", nameof(proposal));
        if (!HasText(proposal.SafetyBufferPolicyVersion, 128))
            throw new ArgumentException("Safety buffer policy version is required.", nameof(proposal));
        if (proposal.PolicyVersions is null || proposal.PolicyVersions.Count == 0 ||
            proposal.PolicyVersions.Any(entry => !HasText(entry.Key, 64) || !HasText(entry.Value, 128)))
            throw new ArgumentException("At least one valid policy version is required.", nameof(proposal));
        if (proposal.ReasonCodes is null ||
            proposal.ReasonCodes.Count == 0 ||
            proposal.ReasonCodes.Any(code => string.IsNullOrWhiteSpace(code) || !ReasonCodePattern.IsMatch(code)) ||
            proposal.ReasonCodes.Distinct(StringComparer.Ordinal).Count() != proposal.ReasonCodes.Count)
            throw new ArgumentException("One or more unique canonical reason codes are required.", nameof(proposal));
    }

    private static bool HasText(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maxLength &&
        !value.Any(char.IsControl);
}
