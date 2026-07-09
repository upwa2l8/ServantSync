using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public class SlotInterestService : ISlotInterestService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly ILogger<SlotInterestService> _log;

    public SlotInterestService(
        IDbContextFactory<ApplicationDbContext> factory,
        ILogger<SlotInterestService> log)
    {
        _factory = factory;
        _log = log;
    }

    public async Task<SlotInterestJoinResult> SubscribeAsync(
        string? callerUserId,
        string? personUserId,
        int slotId,
        SlotInterestSource source = SlotInterestSource.Explicit,
        CancellationToken ct = default)
    {
        // Validation: empty caller or target → permission denied (no
        // legitimate subscribe path). PermissionDenied (not AlreadySubscribed)
        // because the caller can't act on their own behalf without an
        // identity, treating this as "no permission" is friendlier than
        // "the subscription already exists" (which it can't — there's no caller).
        if (string.IsNullOrEmpty(callerUserId) || string.IsNullOrEmpty(personUserId))
            return SlotInterestJoinResult.PermissionDenied;

        await using var db = await _factory.CreateDbContextAsync(ct);

        // Look up the slot first. Two reasons: (a) we need its
        // Ministry.OrganizationId to enforce the caller-must-be-in-org
        // gate (ServiceSlot → Ministry → Organization chain), and
        // (b) the gate "refuses on missing slot" is friendlier with a
        // distinct enum value than collapsing into NotFound later.
        var slot = await db.ServiceSlots
            .Include(s => s.Ministry)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null)
        {
            _log.LogDebug("SubscribeAsync: slot {SlotId} not found.", slotId);
            return SlotInterestJoinResult.SlotNotFound;
        }

        // Strict per-org sandbox: a volunteer in Org A must not be able
        // to leave preference rows in Org B (which would surface Org B's
        // slot shifts in their /Open "My slots" filter via the next
        // layer of filtering). Same gate model as MinistryInterestService.JoinAsync.
        var callerInOrg = await db.OrganizationMemberships
            .AnyAsync(m => m.OrganizationId == slot.Ministry.OrganizationId
                && m.PersonUserId == callerUserId, ct);
        if (!callerInOrg)
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to subscribe to slot {SlotId} in org {OrganizationId} without membership.",
                callerUserId, slotId, slot.Ministry.OrganizationId);
            return SlotInterestJoinResult.PermissionDenied;
        }

        // Idempotency: composite-unique on (PersonUserId, ServiceSlotId)
        // would also stop a duplicate insert at the DB level, but
        // checking first lets us return AlreadySubscribed rather than
        // tripping the constraint and surfacing a confusing
        // constraint-violation error. Concurrent inserts race past this
        // check; the unique index catches them and the SaveChanges catch
        // below treats them as AlreadySubscribed quietly.
        var alreadySubscribed = await db.SlotInterests
            .AnyAsync(i => i.PersonUserId == personUserId && i.ServiceSlotId == slotId, ct);
        if (alreadySubscribed) return SlotInterestJoinResult.AlreadySubscribed;

        db.SlotInterests.Add(new SlotInterest
        {
            PersonUserId = personUserId,
            ServiceSlotId = slotId,
            SubscribedUtc = DateTime.UtcNow,
            Source = source,
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return SlotInterestJoinResult.Subscribed;
        }
        catch (DbUpdateException ex)
        {
            _log.LogDebug(ex, "Caught DbUpdateException at SaveChanges; treating as AlreadySubscribed.");
            return SlotInterestJoinResult.AlreadySubscribed;
        }
    }

    public async Task<SlotInterestLeaveResult> UnsubscribeAsync(
        string? callerUserId,
        string? personUserId,
        int slotId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(callerUserId) || string.IsNullOrEmpty(personUserId))
            return SlotInterestLeaveResult.PermissionDenied;

        await using var db = await _factory.CreateDbContextAsync(ct);

        var slot = await db.ServiceSlots
            .Include(s => s.Ministry)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null)
            return SlotInterestLeaveResult.SlotNotFound;

        // Same gate as Subscribe: only in-org callers can clear a
        // preference row. Even though subscription rows are personal,
        // allowing an out-of-org caller to delete rows would let
        // cross-org coordinators silently rewrite another tenant's
        // preference data — matches MinistryInterestService.LeaveAsync's
        // exact paranoid stance.
        var callerInOrg = await db.OrganizationMemberships
            .AnyAsync(m => m.OrganizationId == slot.Ministry.OrganizationId
                && m.PersonUserId == callerUserId, ct);
        if (!callerInOrg)
        {
            _log.LogWarning(
                "Permission denied: caller {CallerUserId} attempted to unsubscribe from slot {SlotId} in org {OrganizationId} without membership.",
                callerUserId, slotId, slot.Ministry.OrganizationId);
            return SlotInterestLeaveResult.PermissionDenied;
        }

        var row = await db.SlotInterests
            .FirstOrDefaultAsync(i => i.PersonUserId == personUserId && i.ServiceSlotId == slotId, ct);
        if (row is null) return SlotInterestLeaveResult.NotSubscribed;

        db.SlotInterests.Remove(row);
        await db.SaveChangesAsync(ct);
        return SlotInterestLeaveResult.Unsubscribed;
    }

    public async Task<List<SlotInterest>> ListSubscribedAsync(
        string personUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(personUserId)) return new();
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Eager-load ServiceSlot → Ministry so the Home panel can
        // render the slot name + ministry name without N+1. One DB
        // round-trip serves the entire panel.
        return await db.SlotInterests
            .Include(i => i.ServiceSlot).ThenInclude(s => s.Ministry)
            .Where(i => i.PersonUserId == personUserId)
            .OrderBy(i => i.ServiceSlot.Name)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<List<SlotInterest>> ListForSlotAsync(
        int slotId,
        CancellationToken ct = default)
    {
        if (slotId <= 0) return new();
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Eager-load Person + Person.User (display name + email) AND
        // ServiceSlot so the coord Subscribers(N) panel renders without
        // an N+1 chase. One-shot query mirrors MinistryInterestService.ListForMinistryAsync
        // (the sibling-in-org drill-through is not relevant here because
        // slots don't have a "parent ministry" concept that propagates
        // subscriptions — slot interest is strictly the slot itself).
        IQueryable<SlotInterest> query = db.SlotInterests
            .Include(i => i.Person)
            .Include(i => i.ServiceSlot);
        query = query.Where(i => i.ServiceSlotId == slotId);
        return await query
            .OrderBy(i => i.Person.LastName).ThenBy(i => i.Person.FirstName)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
