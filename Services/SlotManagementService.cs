using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public class SlotManagementService : ISlotManagementService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly IOrgAuthService _orgAuth;
    private readonly ILogger<SlotManagementService> _log;

    public SlotManagementService(
        IDbContextFactory<ApplicationDbContext> factory,
        IOrgAuthService orgAuth,
        ILogger<SlotManagementService> log)
    {
        _factory = factory;
        _orgAuth = orgAuth;
        _log = log;
    }

    public async Task<SlotUpsertResult> UpsertAsync(
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
        CancellationToken ct = default)
    {
        // Input validation: empty caller or empty name → PermissionDenied
        // (no legitimate create/edit path). Same null-or-blank rule as
        // OrganizationMinistryService so the message-bus doesn't leak a
        // different reason for the same denial shape.
        if (string.IsNullOrEmpty(callerUserId) || string.IsNullOrWhiteSpace(name))
            return SlotUpsertResult.PermissionDenied;

        // ── Round-BA split gate ─────────────────────────────────────────
        // Create-path gate: CanManageMinistryAsync (ministry-tier). This
        // covers org Admin/Coordinator + ministry Coordinator + parent-
        // ministry Coordinator. At create-time there's no slot yet so the
        // per-slot CoordinatorPersonUserId check doesn't apply — the
        // delegation happens at the slot tier, not the ministry tier, so
        // there is no slot-tier recipient to honor on creation.
        //
        // Edit-path gate: CanManageSlotAsync (slot-tier). This is what
        // ServiceSlots/Detail.razor + Schedule.razor + ScheduleSeries.razor
        // already use, so adopting it here keeps the slot surfaces
        // consistent. Crucially it also honors the slot's own
        // CoordinatorPersonUserId so a delegated slot coordinator can
        // edit their own slot without being escalated to ministry tier.
        //
        // Both paths intentionally DO NOT widen for SystemAdmin
        // (IsSystemAdminAsync): the round-AZ strict per-org-mutation
        // contract is preserved. SystemAdmin sees every org read-only.
        var gatePassed = slotId is null
            ? await _orgAuth.CanManageMinistryAsync(callerUserId, ministryId, ct)
            : await _orgAuth.CanManageSlotAsync(callerUserId, slotId.Value, ct);
        if (!gatePassed)
        {
            // Two distinct log lines (rather than a ternary template
            // argument) so the placeholder count matches each branch's
            // format string — avoids the "matched message-template
            // arguments" soft warning from Microsoft.Extensions.Logging
            // analyzers, and keeps each message a single coherent
            // sentence. Logging only the structured fields (caller id
            // is a GUID; not PII).
            if (slotId is null)
            {
                _log.LogWarning(
                    "Permission denied: caller {CallerUserId} attempted to create a slot in ministry {MinistryId} (org {OrganizationId}).",
                    callerUserId, ministryId, organizationId);
            }
            else
            {
                _log.LogWarning(
                    "Permission denied: caller {CallerUserId} attempted to edit slot {SlotId} (ministry {MinistryId}, org {OrganizationId}).",
                    callerUserId, slotId.Value, ministryId, organizationId);
            }
            return SlotUpsertResult.PermissionDenied;
        }

        // Normalize the optional coordinator FK at the chokepoint — same
        // helper-shape as OrganizationMinistryService.UpsertAsync. The
        // coordinator picker posts the literal string "" (NOT null)
        // because the page renders <option value="">—none—</option> and
        // an HTML form-string contract. Writing through "" would hit
        // FK_ServiceSlots_People_CoordinatorPersonUserId (no Person has
        // UserId = ""), throwing DbUpdateException. Same defense as the
        // ministry-tier version.
        var normalizedCoordinator = string.IsNullOrWhiteSpace(coordinatorPersonUserId)
            ? null
            : coordinatorPersonUserId;

        await using var db = await _factory.CreateDbContextAsync(ct);

        if (slotId is int editId)
        {
            // Edit path: scope-check the target row to the caller's
            // ministry AND org so a stale id from another
            // ministry/org can't be hidden behind a friendly NotFound.
            var existing = await db.ServiceSlots.FirstOrDefaultAsync(
                s => s.Id == editId && s.MinistryId == ministryId
                    && s.Ministry!.OrganizationId == organizationId, ct);
            if (existing is null) return SlotUpsertResult.NotFound;

            existing.Name = name.Trim();
            existing.Description = description;
            existing.Location = location;
            existing.DefaultDurationMinutes = defaultDurationMinutes > 0 ? defaultDurationMinutes : null;
            existing.IsActive = isActive;
            existing.Capacity = capacity > 0 ? capacity : 1;
            existing.CoordinatorPersonUserId = normalizedCoordinator;
            existing.CoordinatorEmail = coordinatorEmail;
            existing.CoordinatorPhone = coordinatorPhone;
            existing.Icon = icon;
            await db.SaveChangesAsync(ct);
            return SlotUpsertResult.Saved;
        }

        // Create path. Sanity: the ministry the page pointed us at must
        // belong to the org the page pointed us at, otherwise the page
        // has a bug. Reject cleanly so a future bug is debuggable instead
        // of just crashing the user's request.
        var ministry = await db.Ministries.FirstOrDefaultAsync(
            m => m.Id == ministryId && m.OrganizationId == organizationId, ct);
        if (ministry is null) return SlotUpsertResult.NotFound;

        db.ServiceSlots.Add(new ServiceSlot
        {
            MinistryId = ministryId,
            Name = name.Trim(),
            Description = description,
            Location = location,
            DefaultDurationMinutes = defaultDurationMinutes > 0 ? defaultDurationMinutes : null,
            IsActive = isActive,
            Capacity = capacity > 0 ? capacity : 1,
            CoordinatorPersonUserId = normalizedCoordinator,
            CoordinatorEmail = coordinatorEmail,
            CoordinatorPhone = coordinatorPhone,
            Icon = icon,
        });
        await db.SaveChangesAsync(ct);
        return SlotUpsertResult.Saved;
    }
}
