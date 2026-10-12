using Mono.Api.Domain;
using Mono.Api.Security;
using Xunit;

namespace Mono.Api.Tests;

public sealed class DeliveryRequestValidationTests
{
    [Fact]
    public void Delivery_write_scope_is_available_for_internal_key_provisioning()
    {
        Assert.True(MonoApiKeyScopeCatalog.IsSupported("delivery-requests.write"));
        Assert.True(MonoApiKeyScopeCatalog.IsSupported("delivery-requests.read"));
    }

    [Fact]
    public void Valid_request_normalizes_supported_fields()
    {
        var request = ValidRequest() with
        {
            ExternalReference = "  ORDER-1 ",
            RequestedService = " urban_delivery ",
            RequestedPriority = " standard ",
            Pickup = new DeliveryLocationInput { AddressText = "  Store  " },
            Shipment = new DeliveryShipmentInput
            {
                ShipmentModel = " bag_based ",
                BagCount = 2,
                LoadClass = " m "
            }
        };

        Assert.Empty(DeliveryRequestInputValidator.Validate(request));

        var normalized = request.Normalize();
        Assert.Equal("ORDER-1", normalized.ExternalReference);
        Assert.Equal("URBAN_DELIVERY", normalized.RequestedService);
        Assert.Equal("Store", normalized.Pickup!.AddressText);
        Assert.Equal("BAG_BASED", normalized.Shipment!.ShipmentModel);
        Assert.Equal("M", normalized.Shipment.LoadClass);
    }

    [Theory]
    [InlineData("drq_0123456789abcdef0123456789abcdef", true)]
    [InlineData("drq_ABCDEF0123456789ABCDEF0123456789", false)]
    [InlineData("request-1", false)]
    public void Delivery_request_cursor_ids_have_a_strict_format(string value, bool expected) =>
        Assert.Equal(expected, DeliveryRequestInputValidator.IsValidDeliveryRequestId(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("with space")]
    [InlineData("comma,inside")]
    [InlineData("bad/slash")]
    public void Idempotency_key_rejects_invalid_values(string? key) =>
        Assert.False(DeliveryRequestInputValidator.IsValidIdempotencyKey(key));

    [Fact]
    public void Idempotency_key_rejects_values_longer_than_128_characters() =>
        Assert.False(DeliveryRequestInputValidator.IsValidIdempotencyKey(new string('x', 129)));

    [Theory]
    [InlineData("request-1")]
    [InlineData("a.b_c~d")]
    public void Idempotency_key_accepts_token_characters(string key) =>
        Assert.True(DeliveryRequestInputValidator.IsValidIdempotencyKey(key));

    [Fact]
    public void Request_rejects_partial_or_out_of_range_coordinates_and_wrong_service()
    {
        var request = ValidRequest() with
        {
            Pickup = new DeliveryLocationInput { Latitude = 91 },
            RequestedService = "SAME_DAY"
        };

        var errors = DeliveryRequestInputValidator.Validate(request);

        Assert.Contains(errors, error => error.Contains("latitude and longitude", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("between -90 and 90", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("URBAN_DELIVERY", StringComparison.Ordinal));
    }

    [Fact]
    public void Request_rejects_null_required_objects()
    {
        var errors = DeliveryRequestInputValidator.Validate(ValidRequest() with
        {
            Pickup = null,
            Dropoff = null,
            Shipment = null
        });

        Assert.Contains(errors, error => error == "pickup is required.");
        Assert.Contains(errors, error => error == "dropoff is required.");
        Assert.Contains(errors, error => error == "shipment is required.");
    }

    [Fact]
    public void Canonical_json_uses_normalized_values()
    {
        var request = ValidRequest() with { ExternalReference = " ORDER-1 " };
        var canonical = request.ToCanonicalJson();

        Assert.Contains("\"external_reference\":\"ORDER-1\"", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("\"external_reference\":\" ORDER-1 \"", canonical, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipment_facts_normalize_and_accept_optional_package_volume_and_temperature()
    {
        var request = ValidRequest() with
        {
            Shipment = new DeliveryShipmentInput
            {
                ShipmentModel = "BAG_BASED",
                BagCount = 2,
                PackageCount = 3,
                EstimatedWeightKg = 8,
                EstimatedVolumeM3 = 0.025m,
                LoadClass = "M",
                TemperatureClass = " chilled "
            }
        };

        Assert.Empty(DeliveryRequestInputValidator.Validate(request));
        Assert.Equal("CHILLED", request.Normalize().Shipment!.TemperatureClass);
        Assert.Equal(3, request.Normalize().Shipment!.PackageCount);
        Assert.Equal(0.025m, request.Normalize().Shipment!.EstimatedVolumeM3);
    }

    [Fact]
    public void Shipment_facts_reject_non_positive_values_and_unknown_temperature()
    {
        var request = ValidRequest() with
        {
            Shipment = new DeliveryShipmentInput
            {
                ShipmentModel = "BAG_BASED",
                BagCount = 2,
                PackageCount = 0,
                EstimatedVolumeM3 = 0,
                LoadClass = "M",
                TemperatureClass = "ROOM"
            }
        };

        var errors = DeliveryRequestInputValidator.Validate(request);

        Assert.Contains(errors, error => error.Contains("package_count", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("estimated_volume_m3", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("temperature_class", StringComparison.Ordinal));
    }

    [Fact]
    public void Omitted_shipment_facts_do_not_change_legacy_canonical_payload()
    {
        var canonical = ValidRequest().ToCanonicalJson();

        Assert.DoesNotContain("package_count", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("estimated_volume_m3", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("temperature_class", canonical, StringComparison.Ordinal);
    }

    private static DeliveryRequestInput ValidRequest() => new()
    {
        ExternalReference = "ORDER-1",
        BranchId = "br_0123456789abcdef0123456789abcdef",
        Pickup = new DeliveryLocationInput { AddressText = "Store" },
        Dropoff = new DeliveryLocationInput { AddressText = "Customer" },
        Shipment = new DeliveryShipmentInput
        {
            ShipmentModel = "BAG_BASED",
            BagCount = 1,
            LoadClass = "S"
        },
        RequestedService = "URBAN_DELIVERY",
        RequestedPriority = "STANDARD"
    };
}
