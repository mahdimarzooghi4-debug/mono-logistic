using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public sealed record DeliveryRequestInput
{
    [JsonPropertyName("external_reference")]
    public string ExternalReference { get; init; } = string.Empty;

    [JsonPropertyName("branch_id")]
    public string BranchId { get; init; } = string.Empty;

    [JsonPropertyName("pickup")]
    public DeliveryLocationInput? Pickup { get; init; } = new();

    [JsonPropertyName("dropoff")]
    public DeliveryLocationInput? Dropoff { get; init; } = new();

    [JsonPropertyName("shipment")]
    public DeliveryShipmentInput? Shipment { get; init; } = new();

    [JsonPropertyName("requested_service")]
    public string RequestedService { get; init; } = string.Empty;

    [JsonPropertyName("requested_priority")]
    public string RequestedPriority { get; init; } = string.Empty;

    [JsonPropertyName("estimated_ready_at")]
    public DateTimeOffset? EstimatedReadyAt { get; init; }

    public DeliveryRequestInput Normalize() => this with
    {
        ExternalReference = ExternalReference?.Trim() ?? string.Empty,
        BranchId = BranchId?.Trim() ?? string.Empty,
        RequestedService = RequestedService?.Trim().ToUpperInvariant() ?? string.Empty,
        RequestedPriority = RequestedPriority?.Trim().ToUpperInvariant() ?? string.Empty,
        Pickup = Pickup?.Normalize(),
        Dropoff = Dropoff?.Normalize(),
        Shipment = Shipment?.Normalize(),
        EstimatedReadyAt = EstimatedReadyAt?.ToUniversalTime()
    };

    public string ToCanonicalJson() =>
        JsonSerializer.Serialize(Normalize(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

public sealed record DeliveryLocationInput
{
    [JsonPropertyName("address_text")]
    public string? AddressText { get; init; }

    [JsonPropertyName("latitude")]
    public decimal? Latitude { get; init; }

    [JsonPropertyName("longitude")]
    public decimal? Longitude { get; init; }

    [JsonPropertyName("contact_name")]
    public string? ContactName { get; init; }

    [JsonPropertyName("contact_phone")]
    public string? ContactPhone { get; init; }

    [JsonPropertyName("address_details")]
    public string? AddressDetails { get; init; }

    [JsonPropertyName("recipient_name")]
    public string? RecipientName { get; init; }

    [JsonPropertyName("recipient_phone")]
    public string? RecipientPhone { get; init; }

    public DeliveryLocationInput Normalize() => this with
    {
        AddressText = NormalizeOptional(AddressText),
        ContactName = NormalizeOptional(ContactName),
        ContactPhone = NormalizeOptional(ContactPhone),
        AddressDetails = NormalizeOptional(AddressDetails),
        RecipientName = NormalizeOptional(RecipientName),
        RecipientPhone = NormalizeOptional(RecipientPhone)
    };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record DeliveryShipmentInput
{
    [JsonPropertyName("shipment_model")]
    public string ShipmentModel { get; init; } = string.Empty;

    [JsonPropertyName("bag_count")]
    public int BagCount { get; init; }

    [JsonPropertyName("estimated_weight_kg")]
    public decimal? EstimatedWeightKg { get; init; }

    [JsonPropertyName("load_class")]
    public string LoadClass { get; init; } = string.Empty;

    [JsonPropertyName("fragile")]
    public bool Fragile { get; init; }

    [JsonPropertyName("liquid")]
    public bool Liquid { get; init; }

    [JsonPropertyName("keep_upright")]
    public bool KeepUpright { get; init; }

    public DeliveryShipmentInput Normalize() => this with
    {
        ShipmentModel = ShipmentModel?.Trim().ToUpperInvariant() ?? string.Empty,
        LoadClass = LoadClass?.Trim().ToUpperInvariant() ?? string.Empty
    };
}

public static class DeliveryRequestInputValidator
{
    private static readonly Regex BranchIdPattern = new(
        @"\Abr_[0-9a-f]{32}\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private const int MaxTextLength = 500;

    public static IReadOnlyList<string> Validate(DeliveryRequestInput input)
    {
        var errors = new List<string>();
        var normalized = input.Normalize();

        if (normalized.ExternalReference.Length is < 1 or > 128)
            errors.Add("external_reference must contain 1 to 128 characters.");
        if (!BranchIdPattern.IsMatch(normalized.BranchId))
            errors.Add("branch_id must be a valid Mono branch ID.");
        if (normalized.RequestedService != "URBAN_DELIVERY")
            errors.Add("requested_service must be URBAN_DELIVERY in Phase 1.");
        if (normalized.RequestedPriority != "STANDARD")
            errors.Add("requested_priority must be STANDARD in Phase 1.");

        if (normalized.Pickup is not { } pickup)
            errors.Add("pickup is required.");
        else
            ValidateLocation(pickup, "pickup", errors);

        if (normalized.Dropoff is not { } dropoff)
            errors.Add("dropoff is required.");
        else
            ValidateLocation(dropoff, "dropoff", errors);

        if (normalized.Shipment is not { } shipment)
        {
            errors.Add("shipment is required.");
        }
        else
        {
            if (shipment.ShipmentModel != "BAG_BASED")
                errors.Add("shipment.shipment_model must be BAG_BASED in Phase 1.");
            if (shipment.BagCount <= 0)
                errors.Add("shipment.bag_count must be greater than zero.");
            if (shipment.EstimatedWeightKg is <= 0)
                errors.Add("shipment.estimated_weight_kg must be greater than zero when provided.");
            if (shipment.LoadClass is not ("S" or "M" or "L"))
                errors.Add("shipment.load_class must be S, M, or L.");
        }

        return errors;
    }

    public static bool IsValidIdempotencyKey(string? value) =>
        !string.IsNullOrEmpty(value) &&
        value.Length <= 128 &&
        value.All(IsTokenCharacter);

    private static bool IsTokenCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' or '~';

    private static void ValidateLocation(
        DeliveryLocationInput location,
        string field,
        ICollection<string> errors)
    {
        var hasLatitude = location.Latitude.HasValue;
        var hasLongitude = location.Longitude.HasValue;
        if (hasLatitude != hasLongitude)
            errors.Add($"{field} latitude and longitude must be provided together.");
        if (!hasLatitude && string.IsNullOrWhiteSpace(location.AddressText))
            errors.Add($"{field} must include address_text or coordinates.");
        if (location.Latitude is < -90 or > 90)
            errors.Add($"{field}.latitude must be between -90 and 90.");
        if (location.Longitude is < -180 or > 180)
            errors.Add($"{field}.longitude must be between -180 and 180.");

        CheckText(location.AddressText, $"{field}.address_text", errors);
        CheckText(location.ContactName, $"{field}.contact_name", errors);
        CheckText(location.ContactPhone, $"{field}.contact_phone", errors);
        CheckText(location.AddressDetails, $"{field}.address_details", errors);
        CheckText(location.RecipientName, $"{field}.recipient_name", errors);
        CheckText(location.RecipientPhone, $"{field}.recipient_phone", errors);
    }

    private static void CheckText(string? value, string field, ICollection<string> errors)
    {
        if (value?.Length > MaxTextLength)
            errors.Add($"{field} must be no longer than {MaxTextLength} characters.");
    }
}

public sealed record DeliveryRequestResponse(
    [property: JsonPropertyName("delivery_request_id")] string DeliveryRequestId,
    [property: JsonPropertyName("status")] string Status);


public sealed record DeliveryRequestStatusResponse(
    [property: JsonPropertyName("delivery_request_id")] string DeliveryRequestId,
    [property: JsonPropertyName("external_reference")] string ExternalReference,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
