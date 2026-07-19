namespace ServantSync.Services;

/// <summary>Outcome of an attempt to upsert a <see cref="Models.ServiceSlot"/>.</summary>
public enum SlotUpsertResult
{
    /// <summary>The slot row was created or updated.</summary>
    Saved,

    /// <summary>The caller is not authorised for this tier (see CB-style gate notes in SlotManagementService.UpsertAsync).</summary>
    PermissionDenied,

    /// <summary>The caller asked to edit a slot that does not exist (or does not belong to the expected ministry).</summary>
    NotFound,
}

public interface ISlotManagementService
{
    /// <summary>
    /// Create or update a <see cref="Models.ServiceSlot"/> in
    /// <c>(<paramref name="organizationId"/>, <paramref name="ministryId"/>)</c>.
    ///
    /// Round-BA split gates:
    /// <list type="bullet">
    /// <item>
    /// <term>Create path (<paramref name="slotId"/> is null)</term>
    /// <description>The caller must clear <see cref="IOrgAuthService.CanManageMinistryAsync"/>
    /// for <paramref name="ministryId"/> — i.e. be the org's Admin/Coordinator, the
    /// ministry's own Coordinator, or a parent-ministry Coordinator. This is
    /// WIDER than the previous org-Admin-only gate (<see cref="IOrgAuthService.CanManageOrgAsync"/>)
    /// because at create-time there is no per-slot
    /// <c>CoordinatorPersonUserId</c> to delegate to yet.</description>
    /// </item>
    /// <item>
    /// <term>Edit path (<paramref name="slotId"/> is non-null)</term>
    /// <description>The caller must clear <see cref="IOrgAuthService.CanManageSlotAsync"/>
    /// for <paramref name="slotId"/>. This adds the slot's own
    /// <c>CoordinatorPersonUserId</c> on top of the ministry tier, so a
    /// delegated slot coordinator can edit their own slot without needing a
    /// ministry-tier role. Matches the gate already used by
    /// ServiceSlots/Detail.razor, Schedule.razor, and ScheduleSeries.razor.</description>
    /// </item>
    /// </list>
    ///
    /// SystemAdmin does NOT widen either path — the round-AZ strict
    /// per-org-mutation contract for slots is preserved (SystemAdmin
    /// is read-only across arbitrary orgs; the visibility-only exception
    /// is org-create / org-delete / org-edit-tenants).
    ///
    /// Never throws for the expectable failure modes — returns the
    /// outcome, page handlers map to a status message.
    /// </summary>
    Task<SlotUpsertResult> UpsertAsync(
        string? callerUserId,
        int organizationId,
        int ministryId,
        int? slotId,
        string name,
        string? description,
        string? location,
        int defaultDurationMinutes,
        bool isActive,
        int capacity,
        string? coordinatorPersonUserId,
        string? coordinatorEmail,
        string? coordinatorPhone,
        string? icon = null,
        CancellationToken ct = default);
}
