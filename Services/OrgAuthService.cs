using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public interface IOrgAuthService
{
    /// <summary>
    /// Returns the current user's role in the given organization, or null if
    /// they are not a member. Does no authorization on its own — call sites
    /// decide whether they need that role.
    /// </summary>
    Task<OrganizationRole?> GetRoleAsync(string userId, int organizationId, CancellationToken ct = default);

    /// <summary>Round-FR-5: True if the user is the org's Admin.</summary>
    Task<bool> CanManageOrgAsync(string userId, int organizationId, CancellationToken ct = default);

    /// <summary>True if the user is the org's Admin.</summary>
    Task<bool> IsOrgAdminAsync(string userId, int organizationId, CancellationToken ct = default);

    /// <summary>True if the user holds Admin role in any organization.</summary>
    Task<bool> IsAnyOrgAdminAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// True if the user holds Admin, MinistryDirector, or
    /// SlotCoordinator role in any organization. Used by the nav menu
    /// to decide whether to show management-style links the user
    /// actually needs versus links a defacto-volunteer-only user needs
    /// (My schedule, Browse open slots, My training). Round-FR-5
    /// broadened from "Admin or Coordinator" so a Ministry Director
    /// or Slot Coordinator sees their dashboard link.
    /// </summary>
    Task<bool> IsAnyOrgManagerAsync(string userId, CancellationToken ct = default);

    /// <summary>Round-FR-5: True iff the user's role in the given org is
    /// exactly <see cref="OrganizationRole.MinistryDirector"/>.
    /// Drives the Dashboard's ministry-tier query path and the
    /// training-catalog "manage" affordance.</summary>
    Task<bool> IsMinistryDirectorAsync(string userId, int organizationId, CancellationToken ct = default);

    /// <summary>Round-FR-5: True iff the user's role in the given org is
    /// exactly <see cref="OrganizationRole.SlotCoordinator"/>.
    /// Drives the Dashboard's slot-tier query path.</summary>
    Task<bool> IsSlotCoordinatorAsync(string userId, int organizationId, CancellationToken ct = default);

    /// <summary>Round-FR-5: Any-org counterpart of
    /// <see cref="IsMinistryDirectorAsync"/>. Drives nav-menu link
    /// visibility for users who manage ministries without being
    /// Admis (the existing <see cref="IsAnyOrgAdminAsync"/>
    /// stays Admin-only).</summary>
    Task<bool> IsAnyMinistryDirectorAsync(string userId, CancellationToken ct = default);

    /// <summary>Round-FR-5: Any-org counterpart of
    /// <see cref="IsSlotCoordinatorAsync"/>. Drives nav-menu link
    /// visibility for users who coordinate specific slots without any
    /// broader management role.</summary>
    Task<bool> IsAnySlotCoordinatorAsync(string userId, CancellationToken ct = default);

    /// <summary>Round-FR-5: True iff the user can manage the training
    /// catalog for at least one org they belong to (Admin or
    /// MinistryDirector in any org). Drives visibility of the
    /// "In-person training" / "Training/Manage" link in the nav
    /// menu. Slot Coordinators deliberately do NOT count — they manage
    /// slots, not training.</summary>
    Task<bool> IsAnyTrainingManagerAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// True if the user can manage a specific ministry. They are authorized
    /// if they are an Admin of the parent organization, or the
    /// ministry's own <c>CoordinatorPersonUserId</c>, or the coordinator of
    /// its parent ministry (which transitively owns sub-ministries).
    /// Round-FR-5: the per-entity <c>CoordinatorPersonUserId</c> check
    /// already covers Ministry-Directors-of-this-ministry, so an
    /// entity-restricted coordinator continues to pass without
    /// needing org-wide Admin.
    /// </summary>
    Task<bool> CanManageMinistryAsync(string userId, int ministryId, CancellationToken ct = default);

    /// <summary>
    /// True if the user can manage a specific team. They are authorized if
    /// they are an Admin of the parent organization, the
    /// coordinator of the team's ministry, the coordinator of its parent
    /// ministry, or the team's own <c>CoachPersonUserId</c>. Per-entity
    /// coordinator assignments cover Ministry-Directors-of-this-ministry
    /// without needing org-wide Admin.
    /// </summary>
    Task<bool> CanManageTeamAsync(string userId, int teamId, CancellationToken ct = default);

    /// <summary>
    /// True if the user can manage a specific service slot. They are
    /// authorized if they are an Admin of the parent
    /// organization, the coordinator of the slot's ministry, the
    /// coordinator of the slot's parent ministry (transitive), or the
    /// slot's own <c>CoordinatorPersonUserId</c>. Distinct from
    /// <see cref="CanManageOrgAsync"/> because a "Welcome Desk" slot
    /// can be coordinated by Sara even when Sara has no org-wide role;
    /// delegation happens at the slot tier, not the ministry tier.
    /// Round-FR-5: Slot Coordinators are entity-scoped via
    /// <c>ServiceSlot.CoordinatorPersonUserId</c>; the
    /// <c>OrganizationRole.SlotCoordinator</c> membership label is
    /// only a UI-visibility flag (drives the Dashboard's slot-tier
    /// query path), not a write-gate.
    /// </summary>
    Task<bool> CanManageSlotAsync(string userId, int slotId, CancellationToken ct = default);

    /// <summary>
    /// True if the user is the primary contact for any active player on
    /// the given team. Used to gate parent read-only access to the team
    /// page.
    /// </summary>
    Task<bool> IsParentOfAnyPlayerOnTeamAsync(string userId, int teamId, CancellationToken ct = default);

    /// <summary>
    /// True if the user holds the ASP.NET Core Identity role
    /// <c>"SystemAdmin"</c>. SystemAdmin is a strictly visibility-only
    /// tier: it lets the caller see every org/person/assignment via the
    /// UI, but it does NOT widen per-org write gates. To mutate a
    /// specific org's roster / training / schedule the caller still
    /// needs <see cref="IsOrgAdminAsync"/> for THAT org (a regression
    /// here would silently grant god-mode write access to every church
    /// in the database, which is exactly what the user requirement
    /// forbids). The single exception is org-create / org-delete /
    /// org-edit tenants, which IS god-mode for SystemAdmin — see
    /// <c>OrganizationService.CreateOrgAsync</c>.
    /// </summary>
    Task<bool> IsSystemAdminAsync(string userId, CancellationToken ct = default);
}

public class OrgAuthService : IOrgAuthService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    // Cached "SystemAdmin" IdentityRole.Id, resolved lazily on the
    // first IsSystemAdminAsync call. IdentityRole rows rarely change,
    // so a per-app-startup resolution is plenty; the semaphore keeps
    // concurrent first-callers from racing. Stored as empty string
    // (not null) when the role row hasn't been seeded yet so the
    // IsSystemAdminAsync fast-path can return false without re-querying.
    private string _systemAdminRoleId = "";
    private readonly SemaphoreSlim _systemAdminRoleGate = new(1, 1);

    public OrgAuthService(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<OrganizationRole?> GetRoleAsync(string userId, int organizationId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var role = await db.OrganizationMemberships
            .Where(m => m.PersonUserId == userId && m.OrganizationId == organizationId)
            .Select(m => (OrganizationRole?)m.Role)
            .FirstOrDefaultAsync(ct);
        return role;
    }

    public async Task<bool> CanManageOrgAsync(string userId, int organizationId, CancellationToken ct = default)
    {
        // Round-FR-5: Admin only. Ministry Directors and Slot
        // Coordinators manage their assigned entities via
        // Ministry.CoordinatorPersonUserId /
        // ServiceSlot.CoordinatorPersonUserId, NOT via this org-wide
        // gate. Tightening this prevents a Ministry Director from
        // accidentally gaining org-create / org-edit / training-catalog
        // / coordinator-assignment privileges they shouldn't have.
        var role = await GetRoleAsync(userId, organizationId, ct);
        return role == OrganizationRole.Admin;
    }

    public async Task<bool> IsOrgAdminAsync(string userId, int organizationId, CancellationToken ct = default)
    {
        var role = await GetRoleAsync(userId, organizationId, ct);
        return role == OrganizationRole.Admin;
    }

    public async Task<bool> IsAnyOrgAdminAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == userId && m.Role == OrganizationRole.Admin, ct);
    }

    public async Task<bool> IsAnyOrgManagerAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Round-FR-5: widened to include MinistryDirector and
        // SlotCoordinator so a per-ministry / per-slot manager sees
        // their dashboard nav link even when they're not Admin or a
        // generic org Admin/Coordinator. Admin stays in the OR for
        // backwards compatibility with the pre-rename semantics.
        return await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == userId
                && (m.Role == OrganizationRole.Admin
                    || m.Role == OrganizationRole.MinistryDirector
                    || m.Role == OrganizationRole.SlotCoordinator), ct);
    }

    public async Task<bool> IsMinistryDirectorAsync(string userId, int organizationId, CancellationToken ct = default)
    {
        var role = await GetRoleAsync(userId, organizationId, ct);
        return role == OrganizationRole.MinistryDirector;
    }

    public async Task<bool> IsSlotCoordinatorAsync(string userId, int organizationId, CancellationToken ct = default)
    {
        var role = await GetRoleAsync(userId, organizationId, ct);
        return role == OrganizationRole.SlotCoordinator;
    }

    public async Task<bool> IsAnyMinistryDirectorAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == userId && m.Role == OrganizationRole.MinistryDirector, ct);
    }

    public async Task<bool> IsAnySlotCoordinatorAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == userId && m.Role == OrganizationRole.SlotCoordinator, ct);
    }

    public async Task<bool> IsAnyTrainingManagerAsync(string userId, CancellationToken ct = default)
    {
        // Round-FR-5: Admin or MinistryDirector of any org = can
        // manage the training catalog for that org. Slot Coordinators
        // deliberately NOT included — they manage slots, not training.
        // Matches per-spec decision in PLAN.md FR-5 §"Authorization
        // changes" + the per-org counterpart used by the
        // Training/Manage page gate.
        if (string.IsNullOrEmpty(userId)) return false;
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == userId
                && (m.Role == OrganizationRole.Admin
                    || m.Role == OrganizationRole.MinistryDirector), ct);
    }

    public async Task<bool> CanManageMinistryAsync(string userId, int ministryId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Walk up: ministry → (parent ministry) → organization.
        var ministry = await db.Ministries
            .Where(m => m.Id == ministryId)
            .Select(m => new { m.Id, m.OrganizationId, m.CoordinatorPersonUserId, m.ParentMinistryId })
            .FirstOrDefaultAsync(ct);
        if (ministry is null) return false;

        if (ministry.CoordinatorPersonUserId == userId) return true;

        // Org-level admin/coordinator always wins.
        if (await CanManageOrgAsync(userId, ministry.OrganizationId, ct)) return true;

        // Parent ministry coordinator owns its sub-ministries transitively.
        if (ministry.ParentMinistryId.HasValue)
        {
            var parent = await db.Ministries
                .Where(m => m.Id == ministry.ParentMinistryId.Value)
                .Select(m => new { m.CoordinatorPersonUserId })
                .FirstOrDefaultAsync(ct);
            if (parent?.CoordinatorPersonUserId == userId) return true;
        }

        return false;
    }

    public async Task<bool> CanManageTeamAsync(string userId, int teamId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        await using var db = await _factory.CreateDbContextAsync(ct);

        var team = await db.Teams
            .Where(t => t.Id == teamId)
            .Select(t => new { t.Id, t.MinistryId, t.CoachPersonUserId })
            .FirstOrDefaultAsync(ct);
        if (team is null) return false;

        if (team.CoachPersonUserId == userId) return true;
        return await CanManageMinistryAsync(userId, team.MinistryId, ct);
    }

    public async Task<bool> CanManageSlotAsync(string userId, int slotId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Walk up: slot → ministry → (parent ministry) → organization.
        // The slot's own CoordinatorPersonUserId is the cheap shortcut:
        // if the caller IS the slot coordinator, the rest of the chain
        // doesn't matter.
        var slot = await db.ServiceSlots
            .Where(s => s.Id == slotId)
            .Select(s => new { s.Id, s.MinistryId, s.CoordinatorPersonUserId })
            .FirstOrDefaultAsync(ct);
        if (slot is null) return false;

        if (slot.CoordinatorPersonUserId == userId) return true;
        // Otherwise defer to the ministry-tier can-manage check, which
        // already inherits the org-level Admin/Coordinator gate and the
        // parent-ministry transitive rule. Re-implementing those here
        // would risk drifting from the ministry semantics.
        return await CanManageMinistryAsync(userId, slot.MinistryId, ct);
    }

    public async Task<bool> IsParentOfAnyPlayerOnTeamAsync(string userId, int teamId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Players
            .AnyAsync(p => p.TeamId == teamId
                && p.PrimaryContactPersonUserId == userId
                && (p.LeftUtc == null || p.LeftUtc > DateTime.UtcNow), ct);
    }

    public async Task<bool> IsSystemAdminAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        var roleId = await ResolveSystemAdminRoleIdAsync(ct);
        // Empty role id means SystemAdmin wasn't seeded yet — every
        // read returns false rather than querying an unmatchable empty
        // key. The seeder bootstraps the role on every startup, so a
        // production deployment's first call after seeder run will
        // resolve the role id and start returning real answers.
        if (string.IsNullOrEmpty(roleId)) return false;

        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.UserRoles
            .AnyAsync(r => r.UserId == userId && r.RoleId == roleId, ct);
    }

    // Lazy resolution of the SystemAdmin role id. Called on each
    // IsSystemAdminAsync invocation but only ONE database lookup hits
    // IdentityRoles for the lifetime of the app — subsequent calls hit
    // the cached string field. Semaphore gates concurrent first-callers
    // (Blazor InteractiveServer's parallel circuit timing occasionally
    // fires two auth-state refreshes at the same moment).
    private async Task<string> ResolveSystemAdminRoleIdAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(_systemAdminRoleId)) return _systemAdminRoleId;
        await _systemAdminRoleGate.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrEmpty(_systemAdminRoleId)) return _systemAdminRoleId;
            await using var db = await _factory.CreateDbContextAsync(ct);
            var id = await db.Roles
                .Where(r => r.NormalizedName == "SYSTEMADMIN")
                .Select(r => r.Id)
                .FirstOrDefaultAsync(ct);
            _systemAdminRoleId = id ?? "";
            return _systemAdminRoleId;
        }
        finally
        {
            _systemAdminRoleGate.Release();
        }
    }
}
