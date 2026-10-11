using System.Collections.Immutable;
using Mono.Api.Domain;
using Mono.Api.Persistence;
using Mono.Api.Security;

namespace Mono.Api.Services;

public enum DeliveryQuoteIssuanceStatus
{
    Issued,
    Replayed,
    Conflict,
    DeliveryRequestNotFound,
    ExpiryInvalid,
    EvidenceIntegrityInvalid,
    EvidenceSourceUnverified,
    IdempotencyConflict
}

public sealed record DeliveryQuoteIssuanceResult(
    DeliveryQuoteIssuanceStatus Status,
    DeliveryQuoteWriteResult? Persistence);

public interface IDeliveryQuoteStore
{
    Task<DeliveryQuoteWriteResult> PersistAsync(
        DeliveryQuote quote,
        CancellationToken cancellationToken = default);

    Task<DeliveryQuoteWriteResult?> FindIdempotentInitialAsync(
        string deliveryRequestId,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken = default) => Task.FromResult<DeliveryQuoteWriteResult?>(null);

    Task<DeliveryQuoteWriteResult> IssueInitialIdempotentlyAsync(
        DeliveryQuote quote,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken = default) => PersistAsync(quote, cancellationToken);
}

/// <summary>
/// Creates the first immutable Quote version only after the distance envelope is valid
/// and a customer-specific source verifier confirms its attestation.
/// </summary>
public sealed class DeliveryQuoteIssuanceApplicationService
{
    private readonly IDeliveryQuoteStore _store;
    private readonly ICustomerContext _customerContext;
    private readonly IHanaDistanceEvidenceSourceVerifier _sourceVerifier;
    private readonly TimeProvider _timeProvider;

    public DeliveryQuoteIssuanceApplicationService(
        IDeliveryQuoteStore store,
        ICustomerContext customerContext,
        IHanaDistanceEvidenceSourceVerifier sourceVerifier,
        TimeProvider? timeProvider = null)
    {
        _store = store;
        _customerContext = customerContext;
        _sourceVerifier = sourceVerifier;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task<DeliveryQuoteIssuanceResult> IssueInitialAsync(
        string deliveryRequestId,
        DateTimeOffset expiresAt,
        HanaDistancePricingEvidence? evidence,
        CancellationToken cancellationToken = default) =>
        IssueInitialCoreAsync(deliveryRequestId, expiresAt, evidence, null, null, cancellationToken);

    public Task<DeliveryQuoteIssuanceResult> IssueInitialIdempotentlyAsync(
        string deliveryRequestId,
        DateTimeOffset expiresAt,
        HanaDistancePricingEvidence? evidence,
        string idempotencyKey,
        string requestHash,
        CancellationToken cancellationToken = default) =>
        IssueInitialCoreAsync(deliveryRequestId, expiresAt, evidence, idempotencyKey, requestHash, cancellationToken);

    private async Task<DeliveryQuoteIssuanceResult> IssueInitialCoreAsync(
        string deliveryRequestId,
        DateTimeOffset expiresAt,
        HanaDistancePricingEvidence? evidence,
        string? idempotencyKey,
        string? requestHash,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is not null && requestHash is not null)
        {
            var existing = await _store.FindIdempotentInitialAsync(
                deliveryRequestId, idempotencyKey, requestHash, cancellationToken);
            if (existing is not null)
            {
                var replayStatus = existing.Outcome switch
                {
                    DeliveryQuoteWriteOutcome.Replayed => DeliveryQuoteIssuanceStatus.Replayed,
                    DeliveryQuoteWriteOutcome.IdempotencyConflict => DeliveryQuoteIssuanceStatus.IdempotencyConflict,
                    _ => throw new ArgumentOutOfRangeException(
                        nameof(existing), "Idempotency lookup returned an unsupported outcome.")
                };
                return new DeliveryQuoteIssuanceResult(replayStatus, existing);
            }
        }

        if (!HanaDistancePricingEvidenceContract.IsValid(evidence))
        {
            return new DeliveryQuoteIssuanceResult(
                DeliveryQuoteIssuanceStatus.EvidenceIntegrityInvalid, null);
        }

        var issuedAt = _timeProvider.GetUtcNow();
        if (expiresAt == default || expiresAt <= issuedAt)
        {
            return new DeliveryQuoteIssuanceResult(
                DeliveryQuoteIssuanceStatus.ExpiryInvalid, null);
        }

        var customerId = _customerContext.CustomerId;
        var verification = await _sourceVerifier.VerifyAsync(
            customerId, evidence!, cancellationToken);
        if (verification.Status != HanaDistanceEvidenceVerificationStatus.Verified)
        {
            return new DeliveryQuoteIssuanceResult(
                DeliveryQuoteIssuanceStatus.EvidenceSourceUnverified, null);
        }

        var quote = new DeliveryQuote(
            "quote_" + Guid.NewGuid().ToString("N"),
            customerId,
            deliveryRequestId,
            1,
            issuedAt,
            expiresAt.ToUniversalTime(),
            HanaDistancePricingV1.Calculate(evidence!.DistanceMeters),
            ImmutableDictionary<string, string>.Empty.Add(
                "pricing", HanaDistancePricingV1.PolicyVersion),
            ImmutableArray.Create(evidence.EvidenceId),
            new HanaDistancePricingEvidenceReference(
                evidence.EvidenceId,
                evidence.ObservedAt,
                evidence.DistanceMeters,
                evidence.SourceId,
                evidence.PayloadSha256));

        var persistence = idempotencyKey is null || requestHash is null
            ? await _store.PersistAsync(quote, cancellationToken)
            : await _store.IssueInitialIdempotentlyAsync(
                quote, idempotencyKey, requestHash, cancellationToken);
        var status = persistence.Outcome switch
        {
            DeliveryQuoteWriteOutcome.Persisted => DeliveryQuoteIssuanceStatus.Issued,
            DeliveryQuoteWriteOutcome.Replayed => DeliveryQuoteIssuanceStatus.Replayed,
            DeliveryQuoteWriteOutcome.Conflict => DeliveryQuoteIssuanceStatus.Conflict,
            DeliveryQuoteWriteOutcome.IdempotencyConflict => DeliveryQuoteIssuanceStatus.IdempotencyConflict,
            DeliveryQuoteWriteOutcome.DeliveryRequestNotFound =>
                DeliveryQuoteIssuanceStatus.DeliveryRequestNotFound,
            _ => throw new ArgumentOutOfRangeException(
                nameof(persistence), "Quote persistence outcome is not supported.")
        };

        return new DeliveryQuoteIssuanceResult(status, persistence);
    }
}
