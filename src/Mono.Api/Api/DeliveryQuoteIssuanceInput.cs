using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mono.Api.Domain;

namespace Mono.Api.Api;

public sealed record DeliveryQuoteIssuanceInput(
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("distance_evidence")] DeliveryQuoteDistanceEvidenceInput? DistanceEvidence)
{
    public string ComputeRequestHash(string deliveryRequestId, HanaDistancePricingEvidence evidence)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            delivery_request_id = deliveryRequestId,
            expires_at = ExpiresAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            evidence_id = evidence.EvidenceId,
            observed_at = evidence.ObservedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            distance_meters = evidence.DistanceMeters.ToString("G29", CultureInfo.InvariantCulture),
            source_id = evidence.SourceId,
            source_attestation = evidence.SourceAttestation
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }
}

public sealed record DeliveryQuoteDistanceEvidenceInput(
    [property: JsonPropertyName("evidence_id")] string? EvidenceId,
    [property: JsonPropertyName("observed_at")] DateTimeOffset ObservedAt,
    [property: JsonPropertyName("distance_meters")] decimal DistanceMeters,
    [property: JsonPropertyName("source_id")] string? SourceId,
    [property: JsonPropertyName("source_attestation")] string? SourceAttestation)
{
    public HanaDistancePricingEvidence Attach() =>
        HanaDistancePricingEvidenceContract.Attach(
            EvidenceId ?? string.Empty,
            ObservedAt,
            DistanceMeters,
            SourceId ?? string.Empty,
            SourceAttestation ?? string.Empty);
}
