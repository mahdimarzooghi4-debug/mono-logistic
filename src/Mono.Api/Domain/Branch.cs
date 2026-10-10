namespace Mono.Api.Domain;

public sealed record Branch
{
    public string BranchId { get; init; } = string.Empty;
    public string CustomerId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Address { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public string? ZoneId { get; init; }
    public string? OperatingHoursJson { get; init; }
    public string Status { get; init; } = string.Empty;

    public static Branch Create(
        string customerId,
        string name,
        string status,
        string? address = null,
        double? latitude = null,
        double? longitude = null,
        string? zoneId = null,
        string? operatingHoursJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        if (latitude.HasValue != longitude.HasValue)
        {
            throw new ArgumentException("Latitude and longitude must be provided together.");
        }

        if (latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90.");
        }

        if (longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180.");
        }

        return new Branch
        {
            BranchId = $"br_{Guid.NewGuid():N}",
            CustomerId = customerId.Trim(),
            Name = name.Trim(),
            Address = NormalizeOptional(address),
            Latitude = latitude,
            Longitude = longitude,
            ZoneId = NormalizeOptional(zoneId),
            OperatingHoursJson = NormalizeOptional(operatingHoursJson),
            Status = status.Trim()
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
