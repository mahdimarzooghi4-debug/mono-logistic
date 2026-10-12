using System.Text.RegularExpressions;

namespace Mono.Api.Domain;

public enum CourierTrustStatus
{
    Registered,
    IdentityVerified,
    DocumentsSubmitted,
    TrustRequirementsVerified,
    MonoApproved,
    Active
}

public sealed record CourierTrustProfile(
    string CourierId,
    string Mode,
    CourierTrustStatus Status,
    CourierZoneApproval ZoneApproval)
{
}

public sealed record CourierTrustTransition(
    string CourierId,
    CourierTrustStatus FromStatus,
    CourierTrustStatus ToStatus,
    string ActorId,
    string ReasonCode,
    string EvidenceReference,
    DateTimeOffset OccurredAt);

public sealed record CourierTrustTransitionResult(
    CourierTrustProfile Profile,
    CourierTrustTransition Transition);

/// <summary>
/// Applies the ordered Mono courier trust lifecycle. Evidence references point to a
/// separately reviewed source; this domain type does not verify documents or identity.
/// </summary>
public static class CourierTrustLifecycle
{
    private static readonly Regex CourierIdPattern = new(
        @"\Acrr_[0-9a-f]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ReasonCodePattern = new(
        @"\A[A-Z][A-Z0-9_]{1,63}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex EvidenceReferencePattern = new(
        @"\A[A-Za-z0-9][A-Za-z0-9._:/~-]{0,255}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static CourierTrustTransitionResult Advance(
        CourierTrustProfile profile,
        CourierTrustStatus targetStatus,
        string actorId,
        string reasonCode,
        string evidenceReference,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(profile.ZoneApproval);

        if (!CourierIdPattern.IsMatch(profile.CourierId))
            throw new ArgumentException("Courier ID is invalid.", nameof(profile));
        if (string.IsNullOrWhiteSpace(profile.Mode) ||
            profile.Mode.Length > 32 ||
            profile.Mode[0] is < 'A' or > 'Z' ||
            profile.Mode.Any(character =>
                (character is < 'A' or > 'Z') &&
                (character is < '0' or > '9') &&
                character != '_'))
            throw new ArgumentException("Courier mode must be a canonical uppercase identifier.", nameof(profile));
        if (!Enum.IsDefined(profile.Status) || !Enum.IsDefined(targetStatus))
            throw new ArgumentException("Courier trust status is unsupported.", nameof(targetStatus));
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 128 || actorId.Any(char.IsControl))
            throw new ArgumentException("A valid staff actor ID is required.", nameof(actorId));
        if (reasonCode is null || !ReasonCodePattern.IsMatch(reasonCode))
            throw new ArgumentException("A canonical reason code is required.", nameof(reasonCode));
        if (evidenceReference is null || !EvidenceReferencePattern.IsMatch(evidenceReference))
            throw new ArgumentException("A bounded evidence reference is required.", nameof(evidenceReference));
        if (occurredAt == default)
            throw new ArgumentException("Transition time is required.", nameof(occurredAt));

        var expected = Next(profile.Status);
        if (targetStatus != expected)
            throw new InvalidOperationException(
                $"Courier trust cannot move from {profile.Status} to {targetStatus}; expected {expected?.ToString() ?? "no further state"}.");

        var zoneApproval = profile.ZoneApproval.Normalize();
        ValidateZoneApproval(profile.CourierId, zoneApproval);
        if (targetStatus == CourierTrustStatus.Active &&
            (zoneApproval.ApprovedZoneIds.Count == 0 ||
             string.IsNullOrWhiteSpace(zoneApproval.PrimaryZoneId)))
        {
            throw new InvalidOperationException(
                "A courier needs at least one approved operating zone and a primary approved zone before activation.");
        }

        var transition = new CourierTrustTransition(
            profile.CourierId,
            profile.Status,
            targetStatus,
            actorId.Trim(),
            reasonCode,
            evidenceReference,
            occurredAt.ToUniversalTime());

        return new CourierTrustTransitionResult(
            profile with { Status = targetStatus, ZoneApproval = zoneApproval },
            transition);
    }

    private static void ValidateZoneApproval(string courierId, CourierZoneApproval approval)
    {
        if (!string.Equals(approval.CourierId, courierId, StringComparison.Ordinal))
            throw new ArgumentException("Zone approval must belong to this courier.", nameof(approval));

        var preferred = approval.PreferredZoneIds.ToArray();
        var approved = approval.ApprovedZoneIds.ToArray();
        if (preferred.Any(string.IsNullOrWhiteSpace) ||
            approved.Any(string.IsNullOrWhiteSpace) ||
            preferred.Distinct(StringComparer.Ordinal).Count() != preferred.Length ||
            approved.Distinct(StringComparer.Ordinal).Count() != approved.Length)
            throw new ArgumentException("Courier zone IDs must be non-empty and unique.", nameof(approval));

        var preferredSet = preferred.ToHashSet(StringComparer.Ordinal);
        if (approved.Any(zoneId => !preferredSet.Contains(zoneId)))
            throw new ArgumentException("Approved zones must be selected from preferred zones.", nameof(approval));

        if (approval.PrimaryZoneId is not null &&
            !approved.Contains(approval.PrimaryZoneId, StringComparer.Ordinal))
            throw new ArgumentException("Primary zone must be approved.", nameof(approval));
    }

    private static CourierTrustStatus? Next(CourierTrustStatus status) => status switch
    {
        CourierTrustStatus.Registered => CourierTrustStatus.IdentityVerified,
        CourierTrustStatus.IdentityVerified => CourierTrustStatus.DocumentsSubmitted,
        CourierTrustStatus.DocumentsSubmitted => CourierTrustStatus.TrustRequirementsVerified,
        CourierTrustStatus.TrustRequirementsVerified => CourierTrustStatus.MonoApproved,
        CourierTrustStatus.MonoApproved => CourierTrustStatus.Active,
        CourierTrustStatus.Active => null,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };
}
