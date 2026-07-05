using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>Outcome of an attempt to add a person to an organization.</summary>
public enum MemberAddResult
{
    /// <summary>The new membership was inserted.</summary>
    Added,

    /// <summary>The person is already a member of this organization; nothing changed.</summary>
    AlreadyExists,

    /// <summary>The caller is not an Admin of the target organization, or the inputs were invalid.</summary>
    PermissionDenied,
}

/// <summary>Outcome of an attempt to change an existing member's role.</summary>
public enum MemberMutationResult
{
    /// <summary>The role was updated in place.</summary>
    Updated,

    /// <summary>The target person is not a member of this organization.</summary>
    NotFound,

    /// <summary>The caller is not an Admin of the target organization, or the inputs were invalid.</summary>
    PermissionDenied,

    /// <summary>
    /// The caller tried to demote themselves out of <c>Admin</c>. Refused
    /// because a zero-admin org leaves no path to recover management.
    /// A second Admin must demote the first.
    /// </summary>
    SelfDemotionRefused,
}

/// <summary>Outcome of an attempt to remove a member from an organization.</summary>
public enum MemberRemoveResult
{
    /// <summary>The membership row was deleted.</summary>
    Removed,

    /// <summary>The target person is not a member of this organization.</summary>
    NotFound,

    /// <summary>The caller is not an Admin of the target organization, or the inputs were invalid.</summary>
    PermissionDenied,

    /// <summary>
    /// The caller tried to remove an Admin (themselves or another) and that
    /// person is the last remaining Admin of the organization. Refused so
    /// the org never becomes managerless. Add a second Admin (or promote
    /// another member first) before removing.
    /// </summary>
    LastAdminRefused,
}

public interface IMemberManagementService
{
    /// <summary>
    /// Add <paramref name="personUserId"/> to <paramref name="organizationId"/>
    /// with the requested role. Gated: the caller (<paramref name="callerUserId"/>)
    /// must be an Admin of the target organization. Returns the operation
    /// outcome — never throws for permission / duplicate cases; page handlers
    /// surface the message and bail before any insert.
    /// </summary>
    Task<MemberAddResult> AddAsync(
        string? callerUserId,
        int organizationId,
        string? personUserId,
        OrganizationRole role,
        CancellationToken ct = default);

    /// <summary>
    /// Change <paramref name="personUserId"/>'s existing role in
    /// <paramref name="organizationId"/> to <paramref name="newRole"/>.
    /// Gated: caller must be Admin AND not the target (no self-demotion out
    /// of Admin). Returns the mutation outcome — never throws for the
    /// expectable failure modes; page handlers surface a message.
    /// </summary>
    Task<MemberMutationResult> UpdateRoleAsync(
        string? callerUserId,
        int organizationId,
        string? personUserId,
        OrganizationRole newRole,
        CancellationToken ct = default);

    /// <summary>
    /// Remove <paramref name="personUserId"/>'s membership in
    /// <paramref name="organizationId"/>. Gated: caller must be Admin AND
    /// the target must not be the last remaining Admin of the org (any
    /// removal that would leave zero Admins is refused, including
    /// self-removal by the lone Admin). Returns the remove outcome — never
    /// throws for the expectable failure modes; page handlers surface a
    /// message.
    /// </summary>
    Task<MemberRemoveResult> RemoveAsync(
        string? callerUserId,
        int organizationId,
        string? personUserId,
        CancellationToken ct = default);
}

public class MemberManagementService : IMemberManagementService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly IOrgAuthService _orgAuth;
    private readonly ILogger<MemberManagementService> _log;

    public MemberManagementService(
        IDbContextFactory<ApplicationDbContext> factory,
        IOrgAuthService orgAuth,
        ILogger<MemberManagementService> log)
    {
        _factory = factory;
        _orgAuth = orgAuth;
        _log = log;
    }

    public async Task<MemberAddResult> AddAsync(
        string? callerUserId,
        int organizationId,
        string? personUserId,
        OrganizationRole role,
        CancellationToken ct = default)
    {
        // Reject obviously invalid input as a permission-level failure:
        // there's no caller or no target, so there's no legitimate path forward.
        if (string.IsNullOrEmpty(callerUserId) || string.IsNullOrEmpty(personUserId))
            return MemberAddResult.PermissionDenied;

        // Use the canonical admin check (OrgAuthService is the single source
        // of truth for "is this person an admin of this org"). Keeps caching,
        // audit, and any future super-admin override in one place.
        if (!await _orgAuth.IsOrgAdminAsync(callerUserId, organizationId, ct))
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to add {TargetUserId} as {Role} to org {OrganizationId}.",
                callerUserId, personUserId, role, organizationId);
            return MemberAddResult.PermissionDenied;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);

        // Idempotency: composite-unique on (PersonUserId, OrganizationId)
        // would also stop a duplicate insert at the DB level, but checking
        // first lets us return AlreadyExists rather than tripping the
        // constraint and surfacing a confusing constraint-violation error.
        // A concurrent insert can still race past this check; the
        // composite-unique constraint catches it (but currently surfaces as
        // a DbUpdateException, not a clean AlreadyExists). Acceptable for
        // the volume this endpoint sees — flag for concurrent-onboarding
        // revisit if it ever becomes a hot path.
        var alreadyMember = await db.OrganizationMemberships
            .AnyAsync(m => m.OrganizationId == organizationId
                && m.PersonUserId == personUserId, ct);
        if (alreadyMember) return MemberAddResult.AlreadyExists;

        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = organizationId,
            PersonUserId = personUserId,
            Role = role,
            JoinedUtc = DateTime.UtcNow,
        });
        // Concurrent-insert defense: two admins submitting the same
        // (PersonUserId, OrganizationId) at the same moment race past the
        // pre-check above. The composite-unique index catches the second
        // insert. We treat any DbUpdateException at the SaveChanges site
        // as AlreadyExists -- this is intentionally broader than a strict
        // uniqueness sniff because OrganizationMembership only has the
        // composite-unique + the two FKs (Organization, Person), and FK
        // violations are already filtered out by the admin gate and
        // existing Person-existence check. A Debug log on the catch
        // surfaces anything unexpected in dev so it can't silently regress.
        try
        {
            await db.SaveChangesAsync(ct);
            return MemberAddResult.Added;
        }
        catch (DbUpdateException ex)
        {
            _log.LogDebug(ex, "Caught DbUpdateException at SaveChanges; treating as AlreadyExists.");
            return MemberAddResult.AlreadyExists;
        }
    }

    public async Task<MemberMutationResult> UpdateRoleAsync(
        string? callerUserId,
        int organizationId,
        string? personUserId,
        OrganizationRole newRole,
        CancellationToken ct = default)
    {
        // Input validation. Empty caller/target treated as a permission
        // failure (no legitimate path forward from a blank-caller request).
        if (string.IsNullOrEmpty(callerUserId) || string.IsNullOrEmpty(personUserId))
            return MemberMutationResult.PermissionDenied;

        // Admin-gate (single source of truth = OrgAuthService).
        if (!await _orgAuth.IsOrgAdminAsync(callerUserId, organizationId, ct))
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to set role {Role} for {TargetUserId} in org {OrganizationId}.",
                callerUserId, newRole, personUserId, organizationId);
            return MemberMutationResult.PermissionDenied;
        }

        // Self-demotion guard. A single-admin org that lets its only Admin
        // demote themselves becomes unmanageable — there's no path to add
        // another Admin without already being one. Require a second Admin
        // to demote the first.
        if (callerUserId == personUserId && newRole != OrganizationRole.Admin)
        {
            _log.LogWarning(
                "Self-demotion refused: caller {CallerUserId} tried to set their own role to {Role} in org {OrganizationId}.",
                callerUserId, newRole, organizationId);
            return MemberMutationResult.SelfDemotionRefused;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);

        // Look up the target membership row. NotFound covers "target isn't
        // a member of this org" and "id doesn't exist" — same outcome from
        // the page-handler's perspective.
        var row = await db.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId
                && m.PersonUserId == personUserId, ct);
        if (row is null) return MemberMutationResult.NotFound;

        if (row.Role == newRole) return MemberMutationResult.Updated; // no-op success

        row.Role = newRole;
        await db.SaveChangesAsync(ct);
        return MemberMutationResult.Updated;
    }

    public async Task<MemberRemoveResult> RemoveAsync(
        string? callerUserId,
        int organizationId,
        string? personUserId,
        CancellationToken ct = default)
    {
        // Input validation. Empty caller/target treated as a permission
        // failure (no legitimate path forward from a blank-caller request).
        if (string.IsNullOrEmpty(callerUserId) || string.IsNullOrEmpty(personUserId))
            return MemberRemoveResult.PermissionDenied;

        // Admin-gate (single source of truth = OrgAuthService).
        if (!await _orgAuth.IsOrgAdminAsync(callerUserId, organizationId, ct))
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to remove {TargetUserId} from org {OrganizationId}.",
                callerUserId, personUserId, organizationId);
            return MemberRemoveResult.PermissionDenied;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);

        // Look up target membership row. NotFound covers "target isn't a
        // member of this org" and "id doesn't exist" — same page outcome.
        var row = await db.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.OrganizationId == organizationId
                && m.PersonUserId == personUserId, ct);
        if (row is null) return MemberRemoveResult.NotFound;

        // Last-Admin guard. A removal that would leave the org with zero
        // Admins is refused (LastAdminRefused) regardless of whether the
        // target is the caller themselves or a different Admin. Without
        // this, a single-admin org could remove themselves and become
        // managerless; with multi-admin orgs you can rotate Admins freely
        // as long as at least one Admin stays behind.
        if (row.Role == OrganizationRole.Admin
            && await WouldLeaveOrgWithoutAdminAsync(db, organizationId, personUserId, ct))
        {
            _log.LogWarning(
                "Last-Admin removal refused: caller {CallerUserId} tried to remove {TargetUserId} (currently Admin) from org {OrganizationId} when they are the only Admin.",
                callerUserId, personUserId, organizationId);
            return MemberRemoveResult.LastAdminRefused;
        }

        db.OrganizationMemberships.Remove(row);
        await db.SaveChangesAsync(ct);
        return MemberRemoveResult.Removed;
    }

    /// <summary>
    /// Would removing <paramref name="exceptPersonUserId"/> leave the org
    /// with zero Admins? Returns true iff the target is currently an Admin
    /// AND there is no other Admin membership in the org. Caller-side
    /// gate is already applied upstream — this is the post-lookup, pre-
    /// delete invariant check.
    /// </summary>
    private static async Task<bool> WouldLeaveOrgWithoutAdminAsync(
        ApplicationDbContext db,
        int organizationId,
        string exceptPersonUserId,
        CancellationToken ct)
    {
        // Count Admins in this org OTHER than the target. Return true if
        // and only if that count is zero (i.e. the target is the lone Admin).
        var otherAdminCount = await db.OrganizationMemberships
            .CountAsync(m => m.OrganizationId == organizationId
                && m.Role == OrganizationRole.Admin
                && m.PersonUserId != exceptPersonUserId, ct);
        return otherAdminCount == 0;
    }
}
