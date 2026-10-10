namespace Mono.Api.Domain;

public sealed record Customer
{
    public string CustomerId { get; init; } = string.Empty;
    public string LegalName { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? CustomerType { get; init; }
    public string? DefaultChannel { get; init; }
    public string? DefaultCurrency { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public static Customer Create(
        string legalName,
        string status,
        string? displayName = null,
        string? customerType = null,
        string? defaultChannel = null,
        string? defaultCurrency = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legalName);
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        return new Customer
        {
            CustomerId = $"cus_{Guid.NewGuid():N}",
            LegalName = legalName.Trim(),
            DisplayName = NormalizeOptional(displayName),
            Status = status.Trim(),
            CustomerType = NormalizeOptional(customerType),
            DefaultChannel = NormalizeOptional(defaultChannel),
            DefaultCurrency = NormalizeOptional(defaultCurrency),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
