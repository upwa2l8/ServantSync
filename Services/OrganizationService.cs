using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public interface IOrganizationService
{
    /// <summary>
    /// Create a new <see cref="Organization"/> row and atomically insert an
    /// <see cref="OrganizationMembership"/> making the caller its first
    /// Admin. Gated: the caller must already be an Admin of *some*
    /// existing organization (existing rule preserved from the Edit page's
    /// <c>IsAnyOrgAdminAsync</c> gate). Returns the newly-created org id,
    /// or null on failure (caller-not-admin, invalid input, or DB error
    /// surfaces as an exception, but permission denial is in-band via
    /// null + log so the page handler can surface a friendly message).
    /// </summary>
    Task<int?> CreateOrgAsync(
        string? callerUserId,
        string name,
        string? description,
        string? address,
        string? contactEmail,
        string? contactPhone,
        CancellationToken ct = default);

    /// <summary>
    /// Rotate <paramref name="organizationId"/>'s <c>RegistrationToken</c>
    /// to a new GUID-derived value, invalidating any previously-shared
    /// <c>/Account/Register?token=...</c> URL. Gated: the caller must be
    /// <c>Admin</c> of the target organization. Returns the new token on
    /// success or null on permission denied / org not found / invalid input.
    /// </summary>
    Task<string?> GenerateRegistrationTokenAsync(
        string? callerUserId,
        int organizationId,
        CancellationToken ct = default);
}

public class OrganizationService : IOrganizationService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly IOrgAuthService _orgAuth;
    private readonly ILogger<OrganizationService> _log;

    public OrganizationService(
        IDbContextFactory<ApplicationDbContext> factory,
        IOrgAuthService orgAuth,
        ILogger<OrganizationService> log)
    {
        _factory = factory;
        _orgAuth = orgAuth;
        _log = log;
    }

    public async Task<int?> CreateOrgAsync(
        string? callerUserId,
        string name,
        string? description,
        string? address,
        string? contactEmail,
        string? contactPhone,
        CancellationToken ct = default)
    {
        // Reject obviously invalid input in-band: empty caller or empty name
        // is treated as permission-denied (no legitimate create path).
        if (string.IsNullOrEmpty(callerUserId) || string.IsNullOrWhiteSpace(name))
            return null;

        // Preserve the existing "only Admins of some other org can create"
        // rule from Edit.razor's IsAnyOrgAdminAsync gate. Comment in that
        // file explains the bootstrap assumption (seeded admin@demo.local
        // in Demo Church is the typical first caller).
        if (!await _orgAuth.IsAnyOrgAdminAsync(callerUserId, ct))
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to create organization {OrgName}.",
                callerUserId, name);
            return null;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);

        // Wrap both inserts in an explicit transaction so that if the
        // bootstrap membership row fails (FK violation, etc.), the new
        // Organization doesn't get committed in an orphan state with no
        // Admin. EF wraps each individual SaveChanges in its own implicit
        // transaction by default; we need an explicit one to bind them.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Seed a registration token at Create-time so a freshly-spawned
        // tenancy can immediately share a self-signup URL without an
        // extra "rotate token" round-trip. Guid.NewGuid().ToString("N")
        // yields 32 hex chars; collision risk across orgs is negligible
        // (and the unique index catches the astronomically rare dup).
        var org = new Organization
        {
            Name = name.Trim(),
            Description = description,
            Address = address,
            ContactEmail = contactEmail,
            ContactPhone = contactPhone,
            RegistrationToken = Guid.NewGuid().ToString("N"),
        };
        db.Organizations.Add(org);
        await db.SaveChangesAsync(ct);

        // The creator is bootstrap Admin of their new org. Doing this in
        // the service (not in the page handler) means the contract is
        // unit-testable and reusable if a future "create org" entry point
        // is added (e.g. a CSV import tool or an admin "spawn tenant" UI).
        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            PersonUserId = callerUserId,
            Role = OrganizationRole.Admin,
            JoinedUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
        return org.Id;
    }

    public async Task<string?> GenerateRegistrationTokenAsync(
        string? callerUserId,
        int organizationId,
        CancellationToken ct = default)
    {
        // Reject obviously invalid input as a permission-level failure so
        // page handlers can surface the same "you lack permission" message
        // they use for the rest of the org-admin gates.
        if (string.IsNullOrEmpty(callerUserId)) return null;

        // Admin-only: rotating the registration token is a privileged
        // action (it invalidates any pending invites / shared links).
        // Single source of truth = OrgAuthService.
        if (!await _orgAuth.IsOrgAdminAsync(callerUserId, organizationId, ct))
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to rotate registration token for org {OrganizationId}.",
                callerUserId, organizationId);
            return null;
        }

        await using var db = await _factory.CreateDbContextAsync(ct);
        var org = await db.Organizations.FindAsync(new object?[] { organizationId }, ct);
        if (org is null)
        {
            _log.LogWarning("GenerateRegistrationTokenAsync: organization {OrganizationId} not found.", organizationId);
            return null;
        }

        org.RegistrationToken = Guid.NewGuid().ToString("N");
        await db.SaveChangesAsync(ct);
        return org.RegistrationToken;
    }
}
