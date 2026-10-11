using System.Text.Json.Serialization;
using Mono.Api.Domain;

namespace Mono.Api.Api;

public sealed record PromiseCommitInput(
    [property: JsonPropertyName("requested_criteria")] IReadOnlyList<PromiseCriterionInput>? RequestedCriteria,
    [property: JsonPropertyName("requested_delivery_at")] DateTimeOffset? RequestedDeliveryAt,
    [property: JsonPropertyName("alternative_search")] PromiseAlternativeSearchInput? AlternativeSearch,
    [property: JsonPropertyName("proposal")] PromiseCommitProposalInput? Proposal)
{
    public IReadOnlyList<FeasibilityCriterionResult>? ToRequestedCriteria() =>
        RequestedCriteria?.Select(criteria => criteria.ToDomain()).ToArray();

    public AlternativePromiseSearchEvidence? ToAlternativeSearch() =>
        AlternativeSearch?.ToDomain();

    public PromiseCommitProposal ToProposal(string deliveryRequestId, string decisionId)
    {
        if (Proposal is null)
            throw new ArgumentException("proposal is required.", nameof(Proposal));

        return new PromiseCommitProposal(
            Proposal.PromiseId ?? string.Empty,
            deliveryRequestId,
            decisionId,
            Proposal.Version,
            Proposal.EvaluatedAt ?? default,
            Proposal.CommittedDeliveryAt ?? default,
            ParseEnum<FeasibilityOutcome>(Proposal.Outcome, nameof(Proposal.Outcome)),
            Proposal.SafetyBuffer,
            Proposal.SafetyBufferPolicyVersion ?? string.Empty,
            Proposal.PolicyVersions ?? new Dictionary<string, string>(),
            Proposal.ReasonCodes ?? []);
    }

    private static TEnum ParseEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum
    {
        if (value is null ||
            !Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) ||
            !string.Equals(Enum.GetName(parsed), value, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{field} is not a supported canonical value.", field);
        }

        return parsed;
    }
}

public sealed record PromiseCriterionInput(
    [property: JsonPropertyName("criterion")] string? Criterion,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("reason_codes")] IReadOnlyList<string>? ReasonCodes,
    [property: JsonPropertyName("integrity")] PromiseEvidenceIntegrityInput? Integrity)
{
    public FeasibilityCriterionResult ToDomain() =>
        new(
            ParseEnum<FeasibilityCriterion>(Criterion, nameof(Criterion)),
            ParseEnum<FeasibilityCriterionStatus>(Status, nameof(Status)),
            ReasonCodes ?? [],
            Integrity?.ToDomain());

    private static TEnum ParseEnum<TEnum>(string? value, string field)
        where TEnum : struct, Enum
    {
        if (value is null ||
            !Enum.TryParse<TEnum>(value, ignoreCase: false, out var parsed) ||
            !string.Equals(Enum.GetName(parsed), value, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{field} is not a supported canonical value.", field);
        }

        return parsed;
    }
}

public sealed record PromiseEvidenceIntegrityInput(
    [property: JsonPropertyName("evidence_id")] string? EvidenceId,
    [property: JsonPropertyName("observed_at")] DateTimeOffset? ObservedAt,
    [property: JsonPropertyName("payload_sha256")] string? PayloadSha256,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("source_attestation")] string? SourceAttestation)
{
    public FeasibilityEvidenceIntegrity ToDomain() =>
        new(
            EvidenceId ?? string.Empty,
            ObservedAt ?? default,
            PayloadSha256 ?? string.Empty,
            SourceId,
            SourceAttestation);
}

public sealed record PromiseAlternativeSearchInput(
    [property: JsonPropertyName("search_id")] string? SearchId,
    [property: JsonPropertyName("searched_at")] DateTimeOffset? SearchedAt,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("proposed_delivery_at")] DateTimeOffset? ProposedDeliveryAt,
    [property: JsonPropertyName("criteria")] IReadOnlyList<PromiseCriterionInput>? Criteria,
    [property: JsonPropertyName("reason_codes")] IReadOnlyList<string>? ReasonCodes)
{
    public AlternativePromiseSearchEvidence ToDomain() =>
        new(
            SearchId ?? string.Empty,
            SearchedAt ?? default,
            ParseStatus(),
            ProposedDeliveryAt,
            Criteria?.Select(criteria => criteria.ToDomain()).ToArray(),
            ReasonCodes ?? []);

    private AlternativePromiseSearchStatus ParseStatus()
    {
        if (Status is null ||
            !Enum.TryParse<AlternativePromiseSearchStatus>(Status, ignoreCase: false, out var parsed) ||
            !string.Equals(Enum.GetName(parsed), Status, StringComparison.Ordinal))
        {
            throw new ArgumentException("status is not a supported canonical alternative-search value.", nameof(Status));
        }

        return parsed;
    }
}

public sealed record PromiseCommitProposalInput(
    [property: JsonPropertyName("promise_id")] string? PromiseId,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("evaluated_at")] DateTimeOffset? EvaluatedAt,
    [property: JsonPropertyName("committed_delivery_at")] DateTimeOffset? CommittedDeliveryAt,
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("safety_buffer")] TimeSpan SafetyBuffer,
    [property: JsonPropertyName("safety_buffer_policy_version")] string? SafetyBufferPolicyVersion,
    [property: JsonPropertyName("policy_versions")] IReadOnlyDictionary<string, string>? PolicyVersions,
    [property: JsonPropertyName("reason_codes")] IReadOnlyList<string>? ReasonCodes);
