using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>Outcome of an attempt to subscribe to a slot.</summary>
public enum SlotInterestJoinResult
{
    /// <summary>The subscription row was inserted.</summary>
    Subscribed,

    /// <summary>
    /// The caller is already subscribed to this slot; nothing changed.
    /// Idempotent for double-clicks and the /Open
    /// Sign-Up → Subscribe follow-up chain (the spec's "no auto-assign"
    /// decision Q4 explicitly considered this race).
    /// </summary>
    AlreadySubscribed,

    /// <summary>
    /// The caller is not a member of the organization that owns the
    /// slot's parent ministry, or the inputs were invalid. Refused:
    /// a strict per-org sandbox stops a volunteer in Org A from
    /// leaving preference rows in Org B (which would surface Org B's
    /// slot in their Open list via the next layer's filter).
    /// </summary>
    PermissionDenied,

    /// <summary>The target slot id doesn't exist.</summary>
    SlotNotFound,
}

/// <summary>Outcome of an attempt to remove a subscription.</summary>
public enum SlotInterestLeaveResult
{
    /// <summary>The subscription row was deleted.</summary>
    Unsubscribed,

    /// <summary>The caller wasn't subscribed to this slot; nothing changed.</summary>
    NotSubscribed,

    /// <summary>The caller is not a member of the organization that owns the slot's parent ministry, or inputs were invalid.</summary>
    PermissionDenied,

    /// <summary>The target slot id doesn't exist.</summary>
    SlotNotFound,
}

public interface ISlotInterestService
{
    /// <summary>
    /// Mark <paramref name="personUserId"/> as subscribed to
    /// <paramref name="slotId"/>. Gated identically to
    /// <c>IMinistryInterestService.JoinAsync</c>: the caller
    /// (<paramref name="callerUserId"/>) must be a member of
    /// the organization owning the slot's parent ministry — distinct
    /// from the RBAC-level checks in <c>MemberManagementService</c>;
    /// a Coordinator can manage a slot without being expected to
    /// volunteer in it, so there's no role requirement here.
    ///
    /// <para>
    /// <paramref name="source"/> defaults to
    /// <see cref="SlotInterestSource.Explicit"/> (volunteer clicked
    /// the Subscribe button). The /Open auto-subscribe path passes
    /// <see cref="SlotInterestSource.AutoFromAssignment"/> per FR-7
    /// spec Q1 decision YES — distinguishing the two in the audit
    /// column so round-2 self-audit / data-quality tooling can
    /// tell them apart. Coordinator-driven
    /// <c>AssignmentService.AssignAsync</c> calls (the management
    /// side from <c>ServiceSlots/Schedule.razor</c>) DO NOT trigger
    /// auto-subscribe — only self-sign-ups through /Open do.
    /// </para>
    /// </summary>
    Task<SlotInterestJoinResult> SubscribeAsync(
        string? callerUserId,
        string? personUserId,
        int slotId,
        SlotInterestSource source = SlotInterestSource.Explicit,
        CancellationToken ct = default);

    /// <summary>
    /// Remove <paramref name="personUserId"/>'s subscription to
    /// <paramref name="slotId"/>. Gated identically to
    /// <see cref="SubscribeAsync"/>. A self-leave is always permitted
    /// (you can always stop following a slot you previously opted into);
    /// leaving on behalf of someone else requires the same
    /// caller-is-in-org gate. Cross-person unsubscribe is page-gated
    /// (slot coordinators see the Subscribers(N) unsubscribe affordance);
    /// the service-side gate mirrors MinistryInterestService's
    /// permissive model for parallelism rather than the stricter spec
    /// recommendation in Q7 (round 2 hardening).
    /// </summary>
    Task<SlotInterestLeaveResult> UnsubscribeAsync(
        string? callerUserId,
        string? personUserId,
        int slotId,
        CancellationToken ct = default);

    /// <summary>
    /// List the slots <paramref name="personUserId"/> has subscribed
    /// to, with the slot and its parent ministry eager-loaded so the
    /// Home panel can render the rows in one DB round-trip. Sorted
    /// alphabetically by slot name for stable UI order.
    /// </summary>
    Task<List<SlotInterest>> ListSubscribedAsync(
        string personUserId,
        CancellationToken ct = default);

    /// <summary>
    /// The inverse-side query: list the people who have subscribed to
    /// <paramref name="slotId"/>. Used by the slot-coord
    /// "Subscribers(N)" panel on <c>ServiceSlots/Detail.razor</c> to
    /// surface volunteers who want to be at this slot without paging
    /// one-by-one through People.
    /// <para>
    /// Caller gating is the page's responsibility
    /// (<c>OrgAuthService.CanManageSlotAsync</c>); this method is
    /// a pure read query mirroring <c>MinistryInterestService.ListForMinistryAsync</c>'s
    /// pattern. Sorted alphabetically by the person's last name
    /// then first name for stable UI order.
    /// </para>
    /// </summary>
    Task<List<SlotInterest>> ListForSlotAsync(
        int slotId,
        CancellationToken ct = default);
}
