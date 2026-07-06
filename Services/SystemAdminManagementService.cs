using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public class SystemAdminManagementService : ISystemAdminManagementService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly IOrgAuthService _orgAuth;
    private readonly ILogger<SystemAdminManagementService> _log;

    /// <summary>
    /// Role-name literal "SystemAdmin" — matches
    /// <c>DatabaseSeeder.EnsureSystemAdminRoleAsync</c>'s
    /// <c>NormalizedName == SYSTEMADMIN</c> lookup key. Single
    /// source-of-truth string kept in sync across the seed path
    /// and the auth path.
    /// </summary>
    private const string SystemAdminRoleName = "SystemAdmin";
    private const string SystemAdminNormalizedName = "SYSTEMADMIN";

    public SystemAdminManagementService(
        IDbContextFactory<ApplicationDbContext> factory,
        IOrgAuthService orgAuth,
        ILogger<SystemAdminManagementService> log)
    {
        _factory = factory;
        _orgAuth = orgAuth;
        _log = log;
    }

    public async Task<SystemAdminGrantResult> GrantAsync(
        string? actorUserId,
        string targetUserId,
        CancellationToken ct = default)
    {
        // Round-BC gate. IsSystemAdminAsync is the round-AW foundation;
        // it's the SOLE source of truth for who can grant / revoke. The
        // page-level IsSystemAdminAsync check on /SystemAdmin is the
        // defense-in-depth counterpart: this service refuses again even
        // if a future refactor accidentally exposes the page without it.
        if (string.IsNullOrEmpty(actorUserId))
        {
            await WriteAuditAsync(null, targetUserId, SystemAdminAuditAction.Grant, false, "PermissionDenied");
            return SystemAdminGrantResult.PermissionDenied;
        }
        if (!await _orgAuth.IsSystemAdminAsync(actorUserId, ct))
        {
            _log.LogWarning(
                "Permission denied: caller {ActorUserId} attempted to grant SystemAdmin to {TargetUserId}.",
                actorUserId, targetUserId);
            await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Grant, false, "PermissionDenied");
            return SystemAdminGrantResult.PermissionDenied;
        }

        if (string.IsNullOrWhiteSpace(targetUserId))
        {
            // Element-level validation only after the actor-gate so an
            // anonymous attacker can't probe the audit log for "what
            // would happen if I sent an empty target".
            await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Grant, false, "TargetUserNotFound");
            return SystemAdminGrantResult.TargetUserNotFound;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (target is null)
        {
            _log.LogWarning("GrantAsync: target {TargetUserId} not found.", targetUserId);
            await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Grant, false, "TargetUserNotFound");
            return SystemAdminGrantResult.TargetUserNotFound;
        }

        var roleId = await ResolveSystemAdminRoleIdAsync(db, ct);
        if (string.IsNullOrEmpty(roleId))
        {
            _log.LogError("GrantAsync: SystemAdmin role row not present; seeder hasn't run.");
            await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Grant, false, "PermissionDenied");
            return SystemAdminGrantResult.PermissionDenied;
        }

        if (await db.UserRoles.AnyAsync(ur => ur.UserId == targetUserId && ur.RoleId == roleId, ct))
        {
            // Idempotent refusal: don't re-add the role to a UserRoles
            // row that already exists. Audit row is still written so
            // "no-op" attempts are visible to ops investigating
            // suspicious patterns.
            await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Grant, false, "AlreadyHasRole");
            return SystemAdminGrantResult.AlreadyHasRole;
        }

        db.UserRoles.Add(new IdentityUserRole<string> { UserId = targetUserId, RoleId = roleId });
        await db.SaveChangesAsync(ct);

        await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Grant, true, null);
        _log.LogInformation(
            "SystemAdmin granted: actor {ActorUserId} granted SystemAdmin to {TargetUserId}.",
            actorUserId, targetUserId);
        return SystemAdminGrantResult.Success;
    }

    public async Task<SystemAdminRevokeResult> RevokeAsync(
        string? actorUserId,
        string targetUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(actorUserId))
        {
            await WriteAuditAsync(null, targetUserId, SystemAdminAuditAction.Revoke, false, "PermissionDenied");
            return SystemAdminRevokeResult.PermissionDenied;
        }
        if (!await _orgAuth.IsSystemAdminAsync(actorUserId, ct))
        {
            _log.LogWarning(
                "Permission denied: caller {ActorUserId} attempted to revoke SystemAdmin from {TargetUserId}.",
                actorUserId, targetUserId);
            await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Revoke, false, "PermissionDenied");
            return SystemAdminRevokeResult.PermissionDenied;
        }

        if (string.IsNullOrWhiteSpace(targetUserId))
        {
            await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Revoke, false, "TargetUserNotFound");
            return SystemAdminRevokeResult.TargetUserNotFound;
        }

        // Self-revoke refuses BEFORE the Identity lookup so the actor
        // cannot accidentally lock themselves out of the role with one
        // click. Audit row is still written (with SelfRevokeRefused)
        // so the event is visible to operators investigating spikes.
        // ⚠ Anti-pattern notice: this blanket-refuses ALL self-revoke
        // attempts even when there are other SystemAdmins who'd be
        // happy to grant you back. A future enhancement could check
        // for "is there another live SystemAdmin besides me" and let
        // the actor opt out safely when there is — but that's a
        // round-BC+ enhancement, not core behavior.
        if (string.Equals(actorUserId, targetUserId, StringComparison.Ordinal))
        {
            _log.LogWarning(
                "RevokeAsync: actor {ActorUserId} tried to revoke their own SystemAdmin role; refused.",
                actorUserId);
            await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Revoke, false, "SelfRevokeRefused");
            return SystemAdminRevokeResult.SelfRevokeRefused;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, ct);
        if (target is null)
        {
            _log.LogWarning("RevokeAsync: target {TargetUserId} not found.", targetUserId);
            await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Revoke, false, "TargetUserNotFound");
            return SystemAdminRevokeResult.TargetUserNotFound;
        }

        var roleId = await ResolveSystemAdminRoleIdAsync(db, ct);
        if (string.IsNullOrEmpty(roleId))
        {
            _log.LogError("RevokeAsync: SystemAdmin role row not present; seeder hasn't run.");
            await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Revoke, false, "PermissionDenied");
            return SystemAdminRevokeResult.PermissionDenied;
        }

        var existing = await db.UserRoles.FirstOrDefaultAsync(
            ur => ur.UserId == targetUserId && ur.RoleId == roleId, ct);
        if (existing is null)
        {
            await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Revoke, false, "DoesNotHaveRole");
            return SystemAdminRevokeResult.DoesNotHaveRole;
        }

        db.UserRoles.Remove(existing);
        await db.SaveChangesAsync(ct);

        await WriteAuditAsync(actorUserId, targetUserId, SystemAdminAuditAction.Revoke, true, null);
        _log.LogInformation(
            "SystemAdmin revoked: actor {ActorUserId} revoked SystemAdmin from {TargetUserId}.",
            actorUserId, targetUserId);
        return SystemAdminRevokeResult.Success;
    }

    public async Task<IReadOnlyList<SystemAdminRow>> ListAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var roleId = await ResolveSystemAdminRoleIdAsync(db, ct);
        if (string.IsNullOrEmpty(roleId)) return Array.Empty<SystemAdminRow>();

        var sysadminUserIds = await db.UserRoles
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .ToListAsync(ct);
        if (sysadminUserIds.Count == 0) return Array.Empty<SystemAdminRow>();

        var users = await db.Users
            .Where(u => sysadminUserIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);
        var persons = await db.People
            .Where(p => sysadminUserIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.FirstName, p.LastName })
            .ToListAsync(ct);
        var personByUserId = persons.ToDictionary(p => p.UserId, p => p);
        // Earliest successful grant for each target — that's "when was
        // this user promoted" from the audit log's perspective. Falls
        // back to DateTime.MinValue when no audit row exists yet
        // (e.g. a SystemAdmin whose promotion predates the round-BC
        // migration; the seeder-promoted admin@demo.local has no
        // audit row by design).
        var auditByUserId = await db.SystemAdminGrantAudits
            .Where(a => sysadminUserIds.Contains(a.TargetUserId)
                && a.Success && a.Action == SystemAdminAuditAction.Grant)
            .GroupBy(a => a.TargetUserId)
            .Select(g => new { TargetUserId = g.Key, FirstGrantedUtc = g.Min(a => a.TimestampUtc) })
            .ToDictionaryAsync(g => g.TargetUserId, g => g.FirstGrantedUtc, ct);

        return users
            .OrderBy(u => u.Email)
            .Select(u => new SystemAdminRow
            {
                UserId = u.Id,
                Email = u.Email ?? "",
                DisplayName = personByUserId.TryGetValue(u.Id, out var p)
                    ? $"{p.FirstName} {p.LastName}".Trim()
                    : (u.Email ?? u.Id),
                GrantedUtc = auditByUserId.TryGetValue(u.Id, out var t) ? t : DateTime.MinValue,
            })
            .ToList();
    }

    public async Task<IReadOnlyList<SystemAdminAuditRow>> ListRecentAuditsAsync(
        int count = 50,
        CancellationToken ct = default)
    {
        if (count <= 0) count = 50;
        await using var db = await _factory.CreateDbContextAsync(ct);

        var rows = await db.SystemAdminGrantAudits
            .OrderByDescending(a => a.TimestampUtc)
            .Take(count)
            .ToListAsync(ct);
        if (rows.Count == 0) return Array.Empty<SystemAdminAuditRow>();

        // Denormalize display names for human-readable presentation on
        // the /SystemAdmin page. One users-query and one persons-query
        // is faster than per-row joins. Identity-only users (no Person
        // row) fall back to email; users with no email fall back to id.
        var userIds = rows.SelectMany(r => new[] { r.ActorUserId, r.TargetUserId }).Distinct().ToList();
        var users = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email })
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? "", ct);
        var persons = await db.People
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.FirstName, p.LastName })
            .ToDictionaryAsync(p => p.UserId, p => (p.FirstName, p.LastName), ct);

        static string DisplayName(
            string userId,
            Dictionary<string, string> users,
            Dictionary<string, (string FirstName, string LastName)> persons)
        {
            if (persons.TryGetValue(userId, out var p))
            {
                var n = $"{p.FirstName} {p.LastName}".Trim();
                if (n.Length > 0) return n;
            }
            if (users.TryGetValue(userId, out var e) && !string.IsNullOrEmpty(e)) return e;
            return userId;
        }

        return rows.Select(r => new SystemAdminAuditRow
        {
            Id = r.Id,
            ActorDisplayName = DisplayName(r.ActorUserId, users, persons),
            TargetDisplayName = DisplayName(r.TargetUserId, users, persons),
            Action = r.Action,
            Success = r.Success,
            Reason = r.Reason,
            TimestampUtc = r.TimestampUtc,
        }).ToList();
    }

    /// <summary>
    /// Resolve the SystemAdmin role id from the schema. The seeder
    /// creates this row on startup so a live DB has one; a freshly-
    /// bootstrapped SQLite that hasn't yet completed seeding may not.
    /// Cached at the OrgAuthService level for the LIVE auth path, but
    /// the resolve is repeated here because the service uses a
    /// scoped DbContext (different cache) and the cost of a single
    /// indexed role SELECT is negligible against the cost of an audit
    /// write that would follow.
    /// </summary>
    private static async Task<string> ResolveSystemAdminRoleIdAsync(
        ApplicationDbContext db, CancellationToken ct = default) =>
        await db.Roles
            .Where(r => r.NormalizedName == SystemAdminNormalizedName)
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct) ?? "";

    /// <summary>
    /// Append-only audit log writer. Every grant/revoke attempt
    /// (success OR failure) calls this. Never throws — audit failure
    /// must not be visible to the page caller as bug 500s.
    /// </summary>
    private async Task WriteAuditAsync(
        string? actorUserId,
        string targetUserId,
        SystemAdminAuditAction action,
        bool success,
        string? reason)
    {
        await using var db = await _factory.CreateDbContextAsync();
        db.SystemAdminGrantAudits.Add(new SystemAdminGrantAudit
        {
            ActorUserId = actorUserId ?? "",
            TargetUserId = targetUserId ?? "",
            Action = action,
            Success = success,
            Reason = reason,
            TimestampUtc = DateTime.UtcNow,
        });
        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Audit failures must NEVER bring down the grant/revoke
            // call itself — the operator's UI sees the result enum and
            // gets a "could not write audit row" deep in the logs.
            _log.LogError(ex, "SystemAdminManagementService.WriteAuditAsync failed.");
        }
    }
}
