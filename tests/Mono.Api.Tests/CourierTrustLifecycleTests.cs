using Mono.Api.Domain;
using Xunit;

namespace Mono.Api.Tests;

public sealed class CourierTrustLifecycleTests
{
    private const string CourierId = "crr_0123456789abcdef0123456789abcdef";
    private static readonly DateTimeOffset OccurredAt =
        new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Advances_only_through_the_documented_lifecycle_and_keeps_transition_attribution()
    {
        var profile = NewProfile();
        var expected = new[]
        {
            CourierTrustStatus.IdentityVerified,
            CourierTrustStatus.DocumentsSubmitted,
            CourierTrustStatus.TrustRequirementsVerified,
            CourierTrustStatus.MonoApproved,
            CourierTrustStatus.Active
        };

        foreach (var target in expected)
        {
            var result = CourierTrustLifecycle.Advance(
                profile, target, "staff-42", "REVIEW_COMPLETED", $"case:{target}", OccurredAt);

            Assert.Equal(profile.Status, result.Transition.FromStatus);
            Assert.Equal(target, result.Transition.ToStatus);
            Assert.Equal("staff-42", result.Transition.ActorId);
            Assert.Equal($"case:{target}", result.Transition.EvidenceReference);
            Assert.Equal(OccurredAt, result.Transition.OccurredAt);
            profile = result.Profile;
        }

        Assert.Equal(CourierTrustStatus.Active, profile.Status);
    }

    [Theory]
    [InlineData(CourierTrustStatus.Registered, CourierTrustStatus.DocumentsSubmitted)]
    [InlineData(CourierTrustStatus.IdentityVerified, CourierTrustStatus.TrustRequirementsVerified)]
    [InlineData(CourierTrustStatus.TrustRequirementsVerified, CourierTrustStatus.Active)]
    [InlineData(CourierTrustStatus.Active, CourierTrustStatus.Active)]
    public void Rejects_skipped_or_repeated_transitions(
        CourierTrustStatus current, CourierTrustStatus target)
    {
        var profile = NewProfile() with { Status = current };

        Assert.Throws<InvalidOperationException>(() => CourierTrustLifecycle.Advance(
            profile, target, "staff-42", "REVIEW_COMPLETED", "case:123", OccurredAt));
    }

    [Fact]
    public void Cannot_activate_without_primary_approved_zone()
    {
        var profile = NewProfile() with { Status = CourierTrustStatus.MonoApproved };

        Assert.Throws<InvalidOperationException>(() => CourierTrustLifecycle.Advance(
            profile, CourierTrustStatus.Active, "staff-42", "APPROVED", "case:activation", OccurredAt));
    }

    [Theory]
    [InlineData("")]
    [InlineData("case with spaces")]
    [InlineData("case\n123")]
    public void Requires_a_bounded_opaque_evidence_reference(string evidenceReference)
    {
        Assert.Throws<ArgumentException>(() => CourierTrustLifecycle.Advance(
            NewProfile(),
            CourierTrustStatus.IdentityVerified,
            "staff-42",
            "IDENTITY_REVIEWED",
            evidenceReference,
            OccurredAt));
    }

    [Fact]
    public void Requires_valid_actor_reason_and_timestamp()
    {
        Assert.Throws<ArgumentException>(() => CourierTrustLifecycle.Advance(
            NewProfile(), CourierTrustStatus.IdentityVerified, " ", "IDENTITY_REVIEWED",
            "case:123", OccurredAt));
        Assert.Throws<ArgumentException>(() => CourierTrustLifecycle.Advance(
            NewProfile(), CourierTrustStatus.IdentityVerified, "staff-42", "Identity reviewed",
            "case:123", OccurredAt));
        Assert.Throws<ArgumentException>(() => CourierTrustLifecycle.Advance(
            NewProfile(), CourierTrustStatus.IdentityVerified, "staff-42", "IDENTITY_REVIEWED",
            "case:123", default));
    }

    [Fact]
    public void Rejects_malformed_courier_identity()
    {
        var profile = NewProfile() with { CourierId = "courier-1" };

        Assert.Throws<ArgumentException>(() => CourierTrustLifecycle.Advance(
            profile, CourierTrustStatus.IdentityVerified, "staff-42", "IDENTITY_REVIEWED",
            "case:123", OccurredAt));
    }


    [Fact]
    public void Rejects_zone_approval_owned_by_another_courier()
    {
        var profile = NewProfile() with
        {
            ZoneApproval = new CourierZoneApproval
            {
                CourierId = "crr_ffffffffffffffffffffffffffffffff",
                PreferredZoneIds = ["zone-central"],
                ApprovedZoneIds = ["zone-central"],
                PrimaryZoneId = "zone-central"
            }
        };

        Assert.Throws<ArgumentException>(() => CourierTrustLifecycle.Advance(
            profile, CourierTrustStatus.IdentityVerified, "staff-42", "IDENTITY_REVIEWED",
            "case:123", OccurredAt));
    }

    [Fact]
    public void Rejects_noncanonical_mode()
    {
        var profile = NewProfile() with { Mode = "Bike" };

        Assert.Throws<ArgumentException>(() => CourierTrustLifecycle.Advance(
            profile, CourierTrustStatus.IdentityVerified, "staff-42", "IDENTITY_REVIEWED",
            "case:123", OccurredAt));
    }

    private static CourierTrustProfile NewProfile() => new(
        CourierId,
        "BIKE",
        CourierTrustStatus.Registered,
        new CourierZoneApproval
        {
            CourierId = CourierId,
            PreferredZoneIds = ["zone-central"],
            ApprovedZoneIds = ["zone-central"],
            PrimaryZoneId = "zone-central"
        });
}
