using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>Outcome of an attempt to mark a ministry as "interested".</summary>
public enum MinistryInterestJoinResult
{
    /// <summary>The interest row was inserted.</summary>
    Joined,

    /// <summary>The caller is already marked as interested in this ministry; nothing changed.</summary>
    AlreadyJoined,

    /// <summary>
    /// The caller is not a member of the organization that owns the
    /// ministry, or the inputs were invalid. Refused: a strict
    /// per-org sandbox stops a volunteer in Org A from leaving
    /// preference rows in Org B (which would surface Org B's slots in
    /// their Open list via the next layer's filter).
    /// </summary>
    PermissionDenied,

    /// <summary>The target ministry id doesn't exist.</summary>
    MinistryNotFound,
}

/// <summary>Outcome of an attempt to remove a "interested" mark.</summary>
public enum MinistryInterestLeaveResult
{
    /// <summary>The interest row was deleted.</summary>
    Left,

    /// <summary>The caller wasn't marked as interested in this ministry; nothing changed.</summary>
    NotInterested,

    /// <summary>The caller is not a member of the organization that owns the ministry, or inputs were invalid.</summary>
    PermissionDenied,

    /// <summary>The target ministry id doesn't exist.</summary>
    MinistryNotFound,
}

public interface IMinistryInterestService
{
    /// <summary>
    /// Mark <paramref name="personUserId"/> as interested in
    /// <paramref name="ministryId"/>. Gated: the caller
    /// (<paramref name="callerUserId"/>) must be a member of the
    /// organization that owns the ministry — distinct from the
    /// RBAC-level checks in <c>MemberManagementService</c>; a
    /// Coordinator can manage a ministry without being expected to
    /// volunteer in it, so there's no role requirement here.
    /// </summary>
    Task<MinistryInterestJoinResult> JoinAsync(
        string? callerUserId,
        string? personUserId,
        int ministryId,
        CancellationToken ct = default);

    /// <summary>
    /// Remove <paramref name="personUserId"/>'s interest in
    /// <paramref name="ministryId"/>. Gated identically to
    /// <see cref="JoinAsync"/>. A self-leave is always permitted
    /// (you can always stop following a ministry you previously opted
    /// into); leaving on behalf of someone else requires the same
    /// caller-is-in-org gate.
    /// </summary>
    Task<MinistryInterestLeaveResult> LeaveAsync(
        string? callerUserId,
        string? personUserId,
        int ministryId,
        CancellationToken ct = default);

    /// <summary>
    /// List the ministries <paramref name="personUserId"/> has marked
    /// as interested, with the ministry and its owning org eager-loaded
    /// so the Home panel can render the rows in one DB round-trip.
    /// Sorted alphabetically by ministry name for stable UI order.
    /// </summary>
    Task<List<MinistryInterest>> ListJoinedAsync(
        string personUserId,
        CancellationToken ct = default);

    /// <summary>
    /// The inverse-side query: list the people who have marked
    /// <paramref name="ministryId"/> as interested (a MinistryInterest
    /// row exists for them). Used by the ministry-coordinator's
    /// "Signups" view to surface the volunteers who have opted in
    /// to this ministry so a coordinator can reach out without
    /// searching People one-by-one.
    ///
    /// <para>
    /// When <paramref name="includeSubMinistries"/> is true, the
    /// query also picks up interests on any DIRECT sub-ministry
    /// (Ministry.ParentMinistryId == ministryId). The Round-AW Signups
    /// page passes true because the user's mental model is "the people
    /// interested in my ministry AND its direct sub-ministries" —
    /// mirroring the parent-coordinator-owns-sub-ministries rule
    /// already baked into <c>OrgAuthService.CanManageMinistryAsync</c>.
    /// The deeper-ancestor case (sub-sub-ministries) is intentionally
    /// NOT included — coordinator ownership is a one-level transitive
    /// rule, not a chain.
    /// </para>
    /// <para>
    /// Caller gating is the page's responsibility
    /// (<c>OrgAuthService.CanManageMinistryAsync</c>); this method is
    /// a pure read query. Sorted alphabetically by the person's last
    /// name then first name for stable UI order.
    /// </para>
    /// </summary>
    Task<List<MinistryInterest>> ListForMinistryAsync(
        int ministryId,
        bool includeSubMinistries,
        CancellationToken ct = default);
}
