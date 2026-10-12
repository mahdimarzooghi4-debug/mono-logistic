using System.Text.Json;

namespace Mono.Api.Domain;

public sealed record CustomerServiceConfiguration(
    string ServiceConfigurationId,
    string CustomerId,
    string ServiceCode,
    int Version,
    string Status,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveTo,
    JsonElement Configuration,
    DateTimeOffset CreatedAt);

public sealed class AmbiguousServiceConfigurationException(string serviceCode, string customerId)
    : InvalidOperationException(
        "More than one active service configuration is effective for customer " +
        customerId + " and service " + serviceCode + ".");
