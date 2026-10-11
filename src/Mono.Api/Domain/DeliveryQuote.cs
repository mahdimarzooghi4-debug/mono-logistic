namespace Mono.Api.Domain;

public sealed record DeliveryQuote(
    string QuoteId,
    string CustomerId,
    string DeliveryRequestId,
    int Version,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt,
    HanaDistanceFare Fare,
    IReadOnlyDictionary<string, string> PolicyVersions,
    IReadOnlyList<string> AuditEvidenceIds);
