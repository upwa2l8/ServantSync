using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public class TrainingDueSoonService : ITrainingDueSoonService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public TrainingDueSoonService(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// RBAC: caller must be Admin OR MinistryDirector in the calling
    /// org (per FR-6 RBAC matrix; mirrors Round-FR-5's restricted
    /// visibility rules and the existing TrainingSessionService +
    /// TrainingService gates). Slot Coordinator and Volunteer denied.
    /// Empty callerUserId also denied. Returns the validated UserId on
    /// success, null on failure so callers throw with a precise reason.
    ///
    /// Why this opens its own DbContext (instead of sharing with the
    /// caller): RBAC + data fetch are intentionally separated so the
    /// caller sees a snapshot AFTER any prior in-flight auth/role state
    /// is settled. Tests that mutate role assignments or completions
    /// between <c>RequireOrgManagerAsync</c> and the data fetch (e.g.
    /// the <c>OneTime_Completed_ForeverValid_NoRow</c> ExpiresUtc wipe)
    /// rely on this isolation. Collapsing to one context looks like a
    /// refactor win but is actually a correctness regression — DON'T.
    /// </summary>
    private async Task<string?> RequireOrgManagerAsync(
        int orgId, string userId, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId)) return null;
        await using var db = await _factory.CreateDbContextAsync(ct);
        var isAllowed = await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == userId
                && m.OrganizationId == orgId
                && (m.Role == OrganizationRole.Admin
                    || m.Role == OrganizationRole.MinistryDirector), ct);
        return isAllowed ? userId : null;
    }

    /// <summary>
    /// Internal snapshot of the most-recent completion per
    /// (person, content) — used by both <see cref="ListAtRiskAsync"/>
    /// and <see cref="ListAtRiskCountsAsync"/> as the cross-product
    /// leaf material. Strongly-typed so subsequent computations don't
    /// need the C# <c>dynamic</c> runtime-binding traps.
    /// </summary>
    private sealed record CompletionSnapshot(
        string PersonUserId,
        int TrainingContentId,
        DateTime CompletionUtc,
        DateTime? ExpiresUtc,
        TrainingCompletionSource CompletionSource);

    public async Task<TrainingDueSoonCounts> ListAtRiskCountsAsync(
        int organizationId, string callerUserId, CancellationToken ct = default)
    {
        _ = await RequireOrgManagerAsync(organizationId, callerUserId, ct)
            ?? throw new UnauthorizedAccessException(
                "Training-due-soon access requires Admin or MinistryDirector in this org.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var window = now.AddDays(30);

        var rows = await BuildAtRiskCrossProductAsync(db, organizationId, now, window, ct);
        var overdue = 0;
        var dueSoon = 0;
        foreach (var row in rows)
        {
            if (row.Status == TrainingDueSoonStatus.Overdue) overdue++;
            else if (row.Status == TrainingDueSoonStatus.DueSoon) dueSoon++;
        }
        return new TrainingDueSoonCounts { OverdueCount = overdue, DueSoonCount = dueSoon };
    }

    public async Task<List<TrainingDueSoonRow>> ListAtRiskAsync(
        int organizationId,
        TrainingDueSoonFilter filter,
        TrainingDueSoonSort sort,
        string callerUserId, CancellationToken ct = default)
    {
        _ = await RequireOrgManagerAsync(organizationId, callerUserId, ct)
            ?? throw new UnauthorizedAccessException(
                "Training-due-soon access requires Admin or MinistryDirector in this org.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var window = now.AddDays(30);

        var allRows = await BuildAtRiskCrossProductAsync(db, organizationId, now, window, ct);

        var filtered = filter switch
        {
            TrainingDueSoonFilter.AllAtRisk => allRows
                .Where(r => r.Status == TrainingDueSoonStatus.Overdue
                         || r.Status == TrainingDueSoonStatus.DueSoon),
            TrainingDueSoonFilter.OverdueOnly => allRows
                .Where(r => r.Status == TrainingDueSoonStatus.Overdue),
            TrainingDueSoonFilter.DueIn30Days => allRows
                .Where(r => r.Status == TrainingDueSoonStatus.DueSoon),
            // Inverse audit: shows rows with a completion in the last 30
            // days EVEN IF they're compliant now. Validates that the
            // training-catalog actually has recent engagement, not just
            // that outstanding requirements exist.
            TrainingDueSoonFilter.CompletedRecently => allRows
                .Where(r => r.LastCompletionUtc.HasValue
                    && (now - r.LastCompletionUtc.Value).TotalDays <= 30),
            _ => allRows,
        };

        // Sort. ByUrgency prioritizes Overdue rows (sorted by
        // days-overdue DESC = most overdue first), then DueSoon rows
        // (sorted by days-until ASC = soonest-due first), then alpha
        // fallback by PersonDisplayName + RequirementTitle.
        var sorted = sort switch
        {
            TrainingDueSoonSort.ByUrgency => filtered
                .OrderBy(r => r.Status == TrainingDueSoonStatus.Overdue ? 0 : 1)
                .ThenByDescending(r => r.DaysDelta is int d && d < 0 ? -d : int.MinValue)
                .ThenBy(r => r.DaysDelta ?? int.MaxValue)
                .ThenBy(r => r.PersonDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.RequirementTitle, StringComparer.OrdinalIgnoreCase),
            TrainingDueSoonSort.ByPersonName => filtered
                .OrderBy(r => r.PersonDisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.RequirementTitle, StringComparer.OrdinalIgnoreCase),
            TrainingDueSoonSort.ByContentTitle => filtered
                .OrderBy(r => r.RequirementTitle, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.PersonDisplayName, StringComparer.OrdinalIgnoreCase),
            _ => filtered,
        };
        return sorted.ToList();
    }

    /// <summary>
    /// Returns every (Person in org, TrainingRequirement in org) row
    /// classified by status. Used by both <see cref="ListAtRiskAsync"/> and
    /// the counts widget. Computed as a cross-product with a left-join on
    /// the most-recent completion per (person, content).
    ///
    /// Why cross-product-then-classify rather than a single chained LINQ:
    /// the status logic has 4 branches (NoCompletion / OneTime-NotRequired /
    /// ExpiredCompletion / WindowCompletion / ValidCompletion) cleaner as
    /// post-filter predicates than as a chained CASE expression, and
    /// the cross-product is bounded by (org people x org requirements)
    /// which is small for the seeded church (4 people x 1-3 requirements).
    /// </summary>
    private async Task<List<TrainingDueSoonRow>> BuildAtRiskCrossProductAsync(
        ApplicationDbContext db,
        int organizationId,
        DateTime now,
        DateTime window,
        CancellationToken ct)
    {
        // Person set: every org member with any role. Stub People ARE
        // included per spec decision Q4 (round-FR-3 stubs are linkable
        // + assigned to ministries + slots; the due-soon view must
        // surface them or coordinators miss them).
        var people = await db.OrganizationMemberships
            .Where(m => m.OrganizationId == organizationId)
            .Select(m => new {
                m.PersonUserId,
                m.Person.FirstName,
                m.Person.LastName,
                m.Person.IsStub,
                m.Person.Email,
            })
            .AsNoTracking()
            .ToListAsync(ct);

        // Requirement set: union of org-scoped + slot-scoped requirements
        // whose slot's ministry lives in this org. Two single-shot queries
        // so we don't ping-pong through a 3-table join with a NULL OR.
        var orgScopedReqs = await db.TrainingRequirements
            .Include(r => r.TrainingContent)
            .Where(r => r.OrganizationId == organizationId)
            .AsNoTracking()
            .ToListAsync(ct);

        var slotMinSlotsIds = await db.ServiceSlots
            .Where(s => s.Ministry.OrganizationId == organizationId)
            .Select(s => s.Id)
            .ToListAsync(ct);

        var slotScopedReqs = slotMinSlotsIds.Count == 0
            ? new List<TrainingRequirement>()
            : await db.TrainingRequirements
                .Include(r => r.TrainingContent)
                .Where(r => r.ServiceSlotId != null
                    && slotMinSlotsIds.Contains(r.ServiceSlotId.Value))
                .AsNoTracking()
                .ToListAsync(ct);

        var reqs = orgScopedReqs.Concat(slotScopedReqs).ToList();
        if (people.Count == 0 || reqs.Count == 0)
            return new List<TrainingDueSoonRow>();

        // Slot name lookup for the scoped-requirement rendering.
        var slotInfo = slotScopedReqs.Count == 0
            ? new Dictionary<int, ServiceSlot>()
            : await db.ServiceSlots
                .Where(s => slotMinSlotsIds.Contains(s.Id))
                .AsNoTracking()
                .ToDictionaryAsync(s => s.Id, s => s, ct);

        // Most-recent completion per (PersonUserId, TrainingContentId).
        // ONE query — GroupBy(...).First() via a join/select projection
        // would be cheaper EF-wise but the simple OrderByDescending +
        // GroupBy shape works on both SQLite and SQL Server (the project's
        // two targets) without dialect-specific syntax.
        var personIds = people.Select(p => p.PersonUserId).ToList();
        var contentIds = reqs.Select(r => r.TrainingContentId).Distinct().ToList();

        var completions = (personIds.Count == 0 || contentIds.Count == 0)
            ? new List<CompletionSnapshot>()
            : await db.TrainingCompletions
                .Where(c => personIds.Contains(c.PersonUserId)
                    && contentIds.Contains(c.TrainingContentId))
                .OrderByDescending(c => c.CompletionUtc)
                .AsNoTracking()
                .Select(c => new CompletionSnapshot(
                    c.PersonUserId,
                    c.TrainingContentId,
                    c.CompletionUtc,
                    c.ExpiresUtc,
                    c.CompletionSource))
                .ToListAsync(ct);

        var mostRecentByKey = completions
            .GroupBy(c => (c.PersonUserId, c.TrainingContentId))
            .ToDictionary(g => g.Key, g => g.First());

        var rows = new List<TrainingDueSoonRow>();
        foreach (var person in people)
        {
            foreach (var req in reqs)
            {
                mostRecentByKey.TryGetValue(
                    (person.PersonUserId, req.TrainingContentId),
                    out var completion);

                var status = ComputeStatus(req, completion, now, window);
                var daysDelta = ComputeDaysDelta(status, completion?.ExpiresUtc, now);

                int? slotIdForReq = req.ServiceSlotId;
                string? slotName = slotIdForReq.HasValue
                    && slotInfo.TryGetValue(slotIdForReq.Value, out var s)
                        ? s.Name
                        : null;
                var scope = req.OrganizationId.HasValue
                    ? "Org"
                    : slotName is not null
                        ? $"Slot · {slotName}"
                        : "Slot";

                rows.Add(new TrainingDueSoonRow
                {
                    PersonUserId = person.PersonUserId,
                    PersonDisplayName = $"{person.FirstName} {person.LastName}".Trim(),
                    IsStub = person.IsStub,
                    EmailAtMoment = string.IsNullOrEmpty(person.Email) ? null : person.Email,
                    RequirementId = req.Id,
                    TrainingContentId = req.TrainingContentId,
                    RequirementTitle = req.TrainingContent?.Title ?? "(unknown content)",
                    RequirementScope = scope,
                    SlotId = slotIdForReq,
                    SlotName = slotName,
                    LastCompletionUtc = completion?.CompletionUtc,
                    ExpiresUtc = completion?.ExpiresUtc,
                    CompletionSource = completion?.CompletionSource,
                    Status = status,
                    DaysDelta = daysDelta,
                });
            }
        }
        return rows;
    }

    /// <summary>
    /// Pure status computation — same logic would be testable in
    /// isolation if a future round breaks it out. The 4 branches mirror
    /// the spec's decision Q5 + OneTime-never-tracked carve-out.
    /// </summary>
    private static TrainingDueSoonStatus ComputeStatus(
        TrainingRequirement req,
        CompletionSnapshot? completion,
        DateTime now,
        DateTime window)
    {
        if (completion is null)
        {
            // Spec decision Q5: OneTime never-tracked = NotRequired
            // (carved out of "at-risk"); Yearly / EveryMonths
            // never-tracked = Overdue ("should have by now").
            return req.Cadence == TrainingCadence.OneTime
                ? TrainingDueSoonStatus.NotRequired
                : TrainingDueSoonStatus.Overdue;
        }
        if (completion.ExpiresUtc is null)
        {
            // OneTime completed: ExpiresUtc is null per the model's
            // "Computed at recording time based on the requirement's
            // cadence" — null means "forever valid". Compliant.
            return TrainingDueSoonStatus.Compliant;
        }
        if (completion.ExpiresUtc < now)
        {
            return TrainingDueSoonStatus.Overdue;
        }
        // Only check window if expiry is in the future. The boundary
        // tests pin this: 29 days = DueSoon, 30 days = DueSoon,
        // 31 days = Compliant (filtered out by default).
        if (completion.ExpiresUtc <= window)
        {
            return TrainingDueSoonStatus.DueSoon;
        }
        return TrainingDueSoonStatus.Compliant;
    }

    /// <summary>
    /// Returns positive days-until-expiry, negative days-overdue. Null
    /// for Compliant + NotRequired (the grid hides the Days column on
    /// those rows; the value is irrelevant). Uses Math.Round so a 6-hour
    /// partial day shows the right day-flip integer; UTC arithmetic.
    /// </summary>
    private static int? ComputeDaysDelta(
        TrainingDueSoonStatus status,
        DateTime? expiresUtc,
        DateTime now)
    {
        if (status != TrainingDueSoonStatus.Overdue && status != TrainingDueSoonStatus.DueSoon)
            return null;
        if (!expiresUtc.HasValue) return null;
        return (int)Math.Round((expiresUtc.Value - now).TotalDays);
    }
}
