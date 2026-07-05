using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>Outcome of an attempt to create an arena.</summary>
public enum ArenaAddResult
{
    /// <summary>The new arena was inserted.</summary>
    Added,

    /// <summary>The caller is not an Admin of the target organization, or the inputs were invalid.</summary>
    PermissionDenied,
}

public interface IArenaService
{
    /// <summary>
    /// Create an <see cref="Arena"/> in <paramref name="organizationId"/>.
    /// Gated: the caller (<paramref name="callerUserId"/>) must be an Admin
    /// of the target organization. Returns the operation outcome — never
    /// throws for permission / invalid-input cases; page handlers surface
    /// the message and bail before any insert.
    /// </summary>
    Task<ArenaAddResult> CreateAsync(
        string? callerUserId,
        int organizationId,
        string name,
        string? surfaceType,
        int? capacity,
        bool isActive,
        CancellationToken ct = default);
}

public class ArenaService : IArenaService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly IOrgAuthService _orgAuth;
    private readonly ILogger<ArenaService> _log;

    public ArenaService(
        IDbContextFactory<ApplicationDbContext> factory,
        IOrgAuthService orgAuth,
        ILogger<ArenaService> log)
    {
        _factory = factory;
        _orgAuth = orgAuth;
        _log = log;
    }

    public async Task<ArenaAddResult> CreateAsync(
        string? callerUserId,
        int organizationId,
        string name,
        string? surfaceType,
        int? capacity,
        bool isActive,
        CancellationToken ct = default)
    {
        // Reject obviously invalid input as a permission-level failure:
        // there's no caller or no org, so there's no legitimate path forward.
        if (string.IsNullOrEmpty(callerUserId)) return ArenaAddResult.PermissionDenied;

        // Use the canonical admin check. Single source of truth for
        // "is this person an admin of this org" lives in OrgAuthService.
        // Coordinator role is NOT sufficient: arena management is a
        // stricter (Admin-only) operation per the new RBAC rules.
        if (!await _orgAuth.IsOrgAdminAsync(callerUserId, organizationId, ct))
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to create arena in org {OrganizationId}.",
                callerUserId, organizationId);
            return ArenaAddResult.PermissionDenied;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);

        // The Arena row's OrganizationId FK is the sole guard against
        // posting to a non-existent org. The IsOrgAdminAsync gate above
        // already filters out non-existent orgs (no membership can match
        // an org that doesn't exist), so the FK will never trip under
        // legitimate use; if it does, the resulting DbUpdateException is
        // a real DB error and should propagate as an unhandled exception
        // rather than be silently masked as PermissionDenied.
        db.Arenas.Add(new Arena
        {
            OrganizationId = organizationId,
            Name = name,
            SurfaceType = surfaceType,
            Capacity = capacity,
            IsActive = isActive,
        });
        await db.SaveChangesAsync(ct);
        return ArenaAddResult.Added;
    }
}
