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
    public bool CanReceiveMissions => Status == CourierTrustStatus.Active;
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
            profile.Mode.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
            throw new ArgumentException("Courier mode must be a canonical identifier.", nameof(profile));
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
        if (targetStatus == CourierTrustStatus.Active &&
            (zoneApproval.ApprovedZoneIds.Count == 0 ||
             string.IsNullOrWhiteSpace(zoneApproval.PrimaryZoneId) ||
             !zoneApproval.ApprovedZoneIds.Contains(zoneApproval.PrimaryZoneId, StringComparer.Ordinal)))
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
