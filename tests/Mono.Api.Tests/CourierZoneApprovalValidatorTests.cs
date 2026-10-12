using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class CourierZoneApprovalValidatorTests
{
    [Fact]
    public void Accepts_approved_zones_selected_from_preferences()
    {
        var errors = CourierZoneApprovalValidator.Validate(
            new CourierZoneApproval
            {
                CourierId = "courier-1",
                PreferredZoneIds = ["micro-1", "micro-2"],
                ApprovedZoneIds = ["micro-1"],
                PrimaryZoneId = "micro-1"
            },
            ZoneRegistry());

        Assert.Empty(errors);
    }

    [Fact]
    public void Normalizes_courier_and_zone_identifiers()
    {
        var approval = new CourierZoneApproval
        {
            CourierId = " courier-1 ",
            PreferredZoneIds = [" micro-1 "],
            ApprovedZoneIds = [" micro-1 "],
            PrimaryZoneId = " micro-1 "
        };

        var normalized = approval.Normalize();

        Assert.Equal("courier-1", normalized.CourierId);
        Assert.Equal(["micro-1"], normalized.PreferredZoneIds);
        Assert.Equal(["micro-1"], normalized.ApprovedZoneIds);
        Assert.Equal("micro-1", normalized.PrimaryZoneId);
        Assert.Empty(CourierZoneApprovalValidator.Validate(approval, ZoneRegistry()));
    }

    [Fact]
    public void Approved_zones_must_be_preferred()
    {
        var errors = CourierZoneApprovalValidator.Validate(
            new CourierZoneApproval
            {
                CourierId = "courier-1",
                PreferredZoneIds = ["micro-1"],
                ApprovedZoneIds = ["micro-2"]
            },
            ZoneRegistry());

        Assert.Contains(errors, error => error.StartsWith("approved_zone_ids must be selected", StringComparison.Ordinal));
    }

    [Fact]
    public void Zone_references_must_exist_in_registry()
    {
        var errors = CourierZoneApprovalValidator.Validate(
            new CourierZoneApproval
            {
                CourierId = "courier-1",
                PreferredZoneIds = ["missing-zone"]
            },
            ZoneRegistry());

        Assert.Contains(errors, error => error.StartsWith("preferred_zone_ids must refer", StringComparison.Ordinal));
    }

    [Fact]
    public void Zone_lists_must_not_contain_blank_or_duplicate_ids()
    {
        var errors = CourierZoneApprovalValidator.Validate(
            new CourierZoneApproval
            {
                CourierId = "courier-1",
                PreferredZoneIds = ["micro-1", " micro-1 ", " "],
                ApprovedZoneIds = ["micro-1", "micro-1"]
            },
            ZoneRegistry());

        Assert.Contains(errors, error => error.StartsWith("preferred_zone_ids values must be unique", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("preferred_zone_ids cannot contain blank", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.StartsWith("approved_zone_ids values must be unique", StringComparison.Ordinal));
    }

    [Fact]
    public void Primary_zone_must_be_approved()
    {
        var errors = CourierZoneApprovalValidator.Validate(
            new CourierZoneApproval
            {
                CourierId = "courier-1",
                PreferredZoneIds = ["micro-1", "micro-2"],
                ApprovedZoneIds = ["micro-1"],
                PrimaryZoneId = "micro-2"
            },
            ZoneRegistry());

        Assert.Contains(errors, error => error.StartsWith("primary_zone_id must refer to an approved", StringComparison.Ordinal));
    }

    [Fact]
    public void Courier_id_is_required()
    {
        var errors = CourierZoneApprovalValidator.Validate(
            new CourierZoneApproval { PreferredZoneIds = ["micro-1"] },
            ZoneRegistry());

        Assert.Contains(errors, error => error == "courier_id is required.");
    }

    [Fact]
    public void Empty_preferences_and_approvals_are_valid()
    {
        Assert.Empty(CourierZoneApprovalValidator.Validate(
            new CourierZoneApproval { CourierId = "courier-1" },
            ZoneRegistry()));
    }

    [Fact]
    public void Null_inputs_are_rejected()
    {
        Assert.Throws<ArgumentNullException>(() =>
            CourierZoneApprovalValidator.Validate(null!, ZoneRegistry()));
        Assert.Throws<ArgumentNullException>(() =>
            CourierZoneApprovalValidator.Validate(new CourierZoneApproval(), null!));
    }

    private static ZoneDefinition[] ZoneRegistry() =>
    [
        new ZoneDefinition { ZoneId = "city-1", Kind = ZoneKind.City, Name = "Tehran" },
        new ZoneDefinition
        {
            ZoneId = "zone-1",
            Kind = ZoneKind.OperatingZone,
            Name = "Central",
            ParentZoneId = "city-1"
        },
        new ZoneDefinition
        {
            ZoneId = "micro-1",
            Kind = ZoneKind.MicroZone,
            Name = "Block A",
            ParentZoneId = "zone-1"
        },
        new ZoneDefinition
        {
            ZoneId = "micro-2",
            Kind = ZoneKind.MicroZone,
            Name = "Block B",
            ParentZoneId = "zone-1"
        }
    ];
}
