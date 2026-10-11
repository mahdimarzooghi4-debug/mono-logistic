using System.Collections.Immutable;

namespace Mono.Api.Domain;

public sealed record DeliveryQuote(
    string QuoteId,
    string CustomerId,
    string DeliveryRequestId,
    int Version,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    HanaDistanceFare Fare,
    ImmutableDictionary<string, string> PolicyVersions,
    ImmutableArray<string> AuditEvidenceIds,
    HanaDistancePricingEvidenceReference? PricingEvidence = null);

public sealed record HanaDistancePricingEvidenceReference(
    string EvidenceId,
    DateTimeOffset ObservedAt,
    decimal DistanceMeters,
    string SourceId,
    string PayloadSha256);
