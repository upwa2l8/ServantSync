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

    /// <summary>True if the user is the org's Admin or Coordinator.</summary>
    Task<bool> CanManageOrgAsync(string userId, int organizationId, CancellationToken ct = default);

    /// <summary>True if the user is the org's Admin.</summary>
    Task<bool> IsOrgAdminAsync(string userId, int organizationId, CancellationToken ct = default);

    /// <summary>True if the user holds Admin role in any organization.</summary>
    Task<bool> IsAnyOrgAdminAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// True if the user holds Admin or Coordinator role in any
    /// organization. Used by the nav menu to decide whether to show links
    /// the user actually needs (Dashboard, Organizations listing, full
    /// People listing, Leagues) versus links a defacto-volunteer-only
    /// user needs (My schedule, Browse open slots, My training).
    /// </summary>
    Task<bool> IsAnyOrgManagerAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// True if the user can manage a specific ministry. They are authorized
    /// if they are an Admin/Coordinator of the parent organization, or the
    /// ministry's own <c>CoordinatorPersonUserId</c>, or the coordinator of
    /// its parent ministry (which transitively owns sub-ministries).
    /// </summary>
    Task<bool> CanManageMinistryAsync(string userId, int ministryId, CancellationToken ct = default);

    /// <summary>
    /// True if the user can manage a specific team. They are authorized if
    /// they are an Admin/Coordinator of the parent organization, the
    /// coordinator of the team's ministry, the coordinator of its parent
    /// ministry, or the team's own <c>CoachPersonUserId</c>.
    /// </summary>
    Task<bool> CanManageTeamAsync(string userId, int teamId, CancellationToken ct = default);

    /// <summary>
    /// True if the user can manage a specific service slot. They are
    /// authorized if they are an Admin/Coordinator of the parent
    /// organization, the coordinator of the slot's ministry, the
    /// coordinator of the slot's parent ministry (transitive), or the
    /// slot's own <c>CoordinatorPersonUserId</c>. Distinct from
    /// <see cref="CanManageOrgAsync"/> because a "Welcome Desk" slot
    /// can be coordinated by Sara even when Sara has no org-wide role;
    /// delegation happens at the slot tier, not the ministry tier.
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
        var role = await GetRoleAsync(userId, organizationId, ct);
        return role == OrganizationRole.Admin || role == OrganizationRole.Coordinator;
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
        return await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == userId
                && (m.Role == OrganizationRole.Admin || m.Role == OrganizationRole.Coordinator), ct);
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
