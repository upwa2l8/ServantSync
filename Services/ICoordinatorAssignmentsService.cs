using Microsoft.EntityFrameworkCore;
using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>
/// Outcome of an attempt to assign or unassign a slot coordinator
/// through the org-wide coordinator dashboard. Avoids exception
/// surfacing for the two expectable failure modes so the Razor page
/// can branch with a friendly message rather than a 500.
/// </summary>
public enum CoordinatorMutationResult
{
    /// <summary>The coordinator triple (Person / Email / Phone) was updated on the slot.</summary>
    Updated,

    /// <summary>The caller is not an Admin/Coordinator of the org that owns the slot.</summary>
    PermissionDenied,

    /// <summary>The slot doesn't exist, or lives in a different org than the caller expects.</summary>
    NotFound,
}

/// <summary>
/// One row in the coordinator dashboard list. Eager-loaded slot,
/// ministry, coordinator person, and the live coordinator contact
/// triple (the slot stores both the FK and the email/phone so the
/// dashboard can still show a meaningful name+contact when the FK
/// points at a Person row that has since been deleted — the FK in
/// this case is null but the email/phone strings persist).
/// </summary>
public record CoordinatorAssignmentRow(
    int SlotId,
    int MinistryId,
    string MinistryName,
    int OrganizationId,
    string SlotName,
    bool IsSlotActive,
    /// <summary>FK People.UserId (null when the row's been cleared
    /// via Unassign or the original Person row was deleted).</summary>
    string? CoordinatorUserId,
    string? CoordinatorDisplayName,
    string? CoordinatorEmail,
    string? CoordinatorPhone);

public interface ICoordinatorAssignmentsService
{
    /// <summary>
    /// Aggregate every slot in the given organization, across all of
    /// the org's ministries, into a single coordinator dashboard
    /// list. Unassigned slots sort first so a quick scan surfaces
    /// what needs attention; assigned slots sort by ministry then
    /// slot name. Read-only — query is purely advisory (the page's
    /// [Authorize] + the implicit signed-in-user requirement are
    /// the only gates; non-members viewing via a stale URL see the
    /// empty list rather than a permission error).
    /// </summary>
    Task<List<CoordinatorAssignmentRow>> ListAsync(int organizationId, CancellationToken ct = default);

    /// <summary>
    /// Update the coordinator triple on a slot. Caller must be an
    /// Admin or Coordinator of the slot's parent org; non-admins
    /// (including the slot coordinator themselves, who can't
    /// re-assign their own slot) get PermissionDenied. Setting
    /// <paramref name="coordinatorUserId"/> to null clears the FK
    /// but keeps the email/phone strings intact — useful when a
    /// coordinator is temporarily unavailable but the contact info
    /// still needs to be visible.
    /// </summary>
    Task<CoordinatorMutationResult> AssignAsync(
        int slotId,
        string? coordinatorUserId,
        string? coordinatorEmail,
        string? coordinatorPhone,
        string callerUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Convenience wrapper for clearing all three coordinator fields
    /// on a slot in a single SaveChanges. Same permission gate as
    /// <see cref="AssignAsync"/>.
    /// </summary>
    Task<CoordinatorMutationResult> UnassignAsync(
        int slotId,
        string callerUserId,
        CancellationToken ct = default);
}

public class CoordinatorAssignmentsService : ICoordinatorAssignmentsService
{
    private readonly IDbContextFactory<Data.ApplicationDbContext> _factory;
    private readonly IOrgAuthService _orgAuth;

    public CoordinatorAssignmentsService(
        IDbContextFactory<Data.ApplicationDbContext> factory,
        IOrgAuthService orgAuth)
    {
        _factory = factory;
        _orgAuth = orgAuth;
    }

    public async Task<List<CoordinatorAssignmentRow>> ListAsync(int organizationId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Walk: org → ministries → slots → coordinator person.
        // The slot table already has a unique (MinistryId, Name)
        // index, so the sort-by-ministry-then-name hits it; the
        // CoordinatorPerson navigation uses the SetNull FK added
        // in round V (mirrors Ministry / Team pattern).
        var rows = await (
            from s in db.ServiceSlots.AsNoTracking()
            join m in db.Ministries.AsNoTracking() on s.MinistryId equals m.Id
            where m.OrganizationId == organizationId
            orderby s.CoordinatorPersonUserId == null ? 0 : 1,
                    m.Name,
                    s.Name
            select new
            {
                s.Id,
                s.MinistryId,
                MinistryName = m.Name,
                m.OrganizationId,
                s.Name,
                s.IsActive,
                s.CoordinatorPersonUserId,
                s.CoordinatorEmail,
                s.CoordinatorPhone,
                CoordFirstName = s.CoordinatorPerson != null ? s.CoordinatorPerson.FirstName : null,
                CoordLastName = s.CoordinatorPerson != null ? s.CoordinatorPerson.LastName : null,
            }).ToListAsync(ct);

        return rows.Select(r => new CoordinatorAssignmentRow(
            SlotId: r.Id,
            MinistryId: r.MinistryId,
            MinistryName: r.MinistryName,
            OrganizationId: r.OrganizationId,
            SlotName: r.Name,
            IsSlotActive: r.IsActive,
            CoordinatorUserId: r.CoordinatorPersonUserId,
            CoordinatorDisplayName: r.CoordFirstName is null
                ? null
                : $"{r.CoordFirstName} {r.CoordLastName}".Trim(),
            CoordinatorEmail: r.CoordinatorEmail,
            CoordinatorPhone: r.CoordinatorPhone)).ToList();
    }

    public async Task<CoordinatorMutationResult> AssignAsync(
        int slotId,
        string? coordinatorUserId,
        string? coordinatorEmail,
        string? coordinatorPhone,
        string callerUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callerUserId)) return CoordinatorMutationResult.PermissionDenied;

        await using var db = await _factory.CreateDbContextAsync(ct);
        // Include the parent ministry so the org-membership gate has the
        // org id in a single round-trip.
        var slot = await db.ServiceSlots
            .Include(s => s.Ministry)
            .FirstOrDefaultAsync(s => s.Id == slotId, ct);
        if (slot is null) return CoordinatorMutationResult.NotFound;

        if (!await _orgAuth.CanManageOrgAsync(callerUserId, slot.Ministry.OrganizationId, ct))
            return CoordinatorMutationResult.PermissionDenied;

        // When a UserId is provided, the coordinator MUST live in the
        // org (they'd be assigned permissions for slots in this org —
        // surprising to admins to see a non-member slot coordinator).
        // Empty / whitespace is treated as an explicit "none" and is
        // always allowed so admins can clear the FK without picking a
        // replacement first.
        if (!string.IsNullOrWhiteSpace(coordinatorUserId))
        {
            var isMember = await db.OrganizationMemberships
                .AnyAsync(m => m.PersonUserId == coordinatorUserId
                    && m.OrganizationId == slot.Ministry.OrganizationId, ct);
            if (!isMember) return CoordinatorMutationResult.PermissionDenied;
        }

        slot.CoordinatorPersonUserId = string.IsNullOrWhiteSpace(coordinatorUserId) ? null : coordinatorUserId;
        slot.CoordinatorEmail = string.IsNullOrWhiteSpace(coordinatorEmail) ? null : coordinatorEmail.Trim();
        slot.CoordinatorPhone = string.IsNullOrWhiteSpace(coordinatorPhone) ? null : coordinatorPhone.Trim();
        await db.SaveChangesAsync(ct);
        return CoordinatorMutationResult.Updated;
    }

    public async Task<CoordinatorMutationResult> UnassignAsync(
        int slotId,
        string callerUserId,
        CancellationToken ct = default)
    {
        // Reuse AssignAsync with all-null inputs; the same gate applies.
        return await AssignAsync(
            slotId,
            coordinatorUserId: null,
            coordinatorEmail: null,
            coordinatorPhone: null,
            callerUserId: callerUserId,
            ct: ct);
    }
}
