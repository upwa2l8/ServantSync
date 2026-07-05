using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public class MinistryInterestService : IMinistryInterestService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly ILogger<MinistryInterestService> _log;

    public MinistryInterestService(
        IDbContextFactory<ApplicationDbContext> factory,
        ILogger<MinistryInterestService> log)
    {
        _factory = factory;
        _log = log;
    }

    public async Task<MinistryInterestJoinResult> JoinAsync(
        string? callerUserId,
        string? personUserId,
        int ministryId,
        CancellationToken ct = default)
    {
        // Validation: empty caller or target → permission denied (no
        // legitimate join path). PermissionDenied (not AlreadyJoined)
        // because the caller can't act on their own behalf without an
        // identity, treating this as "no permission" is friendlier than
        // "the join already exists" (which it can't — there's no caller).
        if (string.IsNullOrEmpty(callerUserId) || string.IsNullOrEmpty(personUserId))
            return MinistryInterestJoinResult.PermissionDenied;

        await using var db = await _factory.CreateDbContextAsync(ct);

        // Look up the ministry first. Two reasons: (a) we need its
        // OrganizationId to enforce the caller-must-be-in-org gate, and
        // (b) the gate "refuses on missing ministry" is friendlier with
        // a distinct enum value than collapsing into NotFound later.
        var ministry = await db.Ministries
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == ministryId, ct);
        if (ministry is null)
        {
            _log.LogDebug("JoinAsync: ministry {MinistryId} not found.", ministryId);
            return MinistryInterestJoinResult.MinistryNotFound;
        }

        // Strict per-org sandbox: a volunteer in Org A must not be able to
        // leave preference rows in Org B (which would surface Org B's
        // open shifts in their /Open list via the next layer of
        // filtering). Same gate model as the cross-org-access default in
        // earlier RBAC rounds.
        var callerInOrg = await db.OrganizationMemberships
            .AnyAsync(m => m.OrganizationId == ministry.OrganizationId
                && m.PersonUserId == callerUserId, ct);
        if (!callerInOrg)
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to join ministry {MinistryId} in org {OrganizationId} without membership.",
                callerUserId, ministryId, ministry.OrganizationId);
            return MinistryInterestJoinResult.PermissionDenied;
        }

        // Idempotency: composite-unique on (PersonUserId, MinistryId)
        // would also stop a duplicate insert at the DB level, but
        // checking first lets us return AlreadyJoined rather than tripping
        // the constraint and surfacing a confusing constraint-violation
        // error. Concurrent inserts race past this check; the unique
        // index catches them and the SaveChanges catch below treats them
        // as AlreadyJoined quietly.
        var alreadyJoined = await db.MinistryInterests
            .AnyAsync(i => i.PersonUserId == personUserId && i.MinistryId == ministryId, ct);
        if (alreadyJoined) return MinistryInterestJoinResult.AlreadyJoined;

        db.MinistryInterests.Add(new MinistryInterest
        {
            PersonUserId = personUserId,
            MinistryId = ministryId,
            JoinedUtc = DateTime.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return MinistryInterestJoinResult.Joined;
        }
        catch (DbUpdateException ex)
        {
            _log.LogDebug(ex, "Caught DbUpdateException at SaveChanges; treating as AlreadyJoined.");
            return MinistryInterestJoinResult.AlreadyJoined;
        }
    }

    public async Task<MinistryInterestLeaveResult> LeaveAsync(
        string? callerUserId,
        string? personUserId,
        int ministryId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(callerUserId) || string.IsNullOrEmpty(personUserId))
            return MinistryInterestLeaveResult.PermissionDenied;

        await using var db = await _factory.CreateDbContextAsync(ct);

        var ministry = await db.Ministries
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == ministryId, ct);
        if (ministry is null)
            return MinistryInterestLeaveResult.MinistryNotFound;

        // Same gate as Join: only in-org callers can clear an interest
        // row. Even though interest rows are personal, allowing an out-of-
        // org caller to delete rows would let cross-org coordinators
        // silently rewrite another tenant's preference data.
        var callerInOrg = await db.OrganizationMemberships
            .AnyAsync(m => m.OrganizationId == ministry.OrganizationId
                && m.PersonUserId == callerUserId, ct);
        if (!callerInOrg)
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to leave ministry {MinistryId} in org {OrganizationId} without membership.",
                callerUserId, ministryId, ministry.OrganizationId);
            return MinistryInterestLeaveResult.PermissionDenied;
        }

        var row = await db.MinistryInterests
            .FirstOrDefaultAsync(i => i.PersonUserId == personUserId && i.MinistryId == ministryId, ct);
        if (row is null) return MinistryInterestLeaveResult.NotInterested;

        db.MinistryInterests.Remove(row);
        await db.SaveChangesAsync(ct);
        return MinistryInterestLeaveResult.Left;
    }

    public async Task<List<MinistryInterest>> ListJoinedAsync(
        string personUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(personUserId)) return new();
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.MinistryInterests
            .Include(i => i.Ministry).ThenInclude(m => m.Organization)
            .Where(i => i.PersonUserId == personUserId)
            .OrderBy(i => i.Ministry.Name)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
