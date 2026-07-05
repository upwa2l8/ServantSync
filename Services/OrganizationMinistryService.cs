using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>Outcome of an attempt to upsert a ministry.</summary>
public enum MinistryUpsertResult
{
    /// <summary>The ministry row was created or updated.</summary>
    Saved,

    /// <summary>The caller is not an Admin of the target organization, or the inputs were invalid.</summary>
    PermissionDenied,

    /// <summary>The caller requested editing a ministry that doesn't exist in the target org.</summary>
    NotFound,
}

public interface IOrganizationMinistryService
{
    /// <summary>
    /// Create or update a <see cref="Ministry"/> in <paramref name="organizationId"/>.
    /// Gated: the caller (<paramref name="callerUserId"/>) must be an Admin
    /// of the target organization. When editing (<paramref name="ministryId"/>
    /// is non-null), the ministry must belong to the same org or the result
    /// is NotFound. Returns the operation outcome — never throws for the
    /// expectable failure modes; page handlers surface a message.
    /// </summary>
    Task<MinistryUpsertResult> UpsertAsync(
        string? callerUserId,
        int organizationId,
        int? ministryId,
        string name,
        string? description,
        string? coordinatorPersonUserId,
        string? coordinatorEmail,
        string? coordinatorPhone,
        CancellationToken ct = default);
}

public class OrganizationMinistryService : IOrganizationMinistryService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly IOrgAuthService _orgAuth;
    private readonly ILogger<OrganizationMinistryService> _log;

    public OrganizationMinistryService(
        IDbContextFactory<ApplicationDbContext> factory,
        IOrgAuthService orgAuth,
        ILogger<OrganizationMinistryService> log)
    {
        _factory = factory;
        _orgAuth = orgAuth;
        _log = log;
    }

    public async Task<MinistryUpsertResult> UpsertAsync(
        string? callerUserId,
        int organizationId,
        int? ministryId,
        string name,
        string? description,
        string? coordinatorPersonUserId,
        string? coordinatorEmail,
        string? coordinatorPhone,
        CancellationToken ct = default)
    {
        // Input validation: empty caller or empty name → permission denied
        // (no legitimate create/edit path).
        if (string.IsNullOrEmpty(callerUserId) || string.IsNullOrWhiteSpace(name))
            return MinistryUpsertResult.PermissionDenied;

        // Admin-gate: the canonical OrgAuthService check is the single
        // source of truth. Coordinator+Admin (CanManageOrgAsync) is NOT
        // sufficient for ministry management anymore — that's the new RBAC.
        if (!await _orgAuth.IsOrgAdminAsync(callerUserId, organizationId, ct))
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to upsert ministry in org {OrganizationId}.",
                callerUserId, organizationId);
            return MinistryUpsertResult.PermissionDenied;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);

        if (ministryId is int editId)
        {
            // Edit path: scope-check the target row to the caller's org so
            // a stale id from another org can't be hidden by a friendly
            // error message. (OrgAuthScope is admin-per-organization, not
            // global, so this matters.)
            var existing = await db.Ministries.FirstOrDefaultAsync(
                m => m.Id == editId && m.OrganizationId == organizationId, ct);
            if (existing is null) return MinistryUpsertResult.NotFound;

            existing.Name = name;
            existing.Description = description;
            existing.CoordinatorPersonUserId = coordinatorPersonUserId;
            existing.CoordinatorEmail = coordinatorEmail;
            existing.CoordinatorPhone = coordinatorPhone;
            await db.SaveChangesAsync(ct);
            return MinistryUpsertResult.Saved;
        }

        // Create path.
        db.Ministries.Add(new Ministry
        {
            OrganizationId = organizationId,
            Name = name.Trim(),
            Description = description,
            CoordinatorPersonUserId = coordinatorPersonUserId,
            CoordinatorEmail = coordinatorEmail,
            CoordinatorPhone = coordinatorPhone,
        });
        await db.SaveChangesAsync(ct);
        return MinistryUpsertResult.Saved;
    }
}
