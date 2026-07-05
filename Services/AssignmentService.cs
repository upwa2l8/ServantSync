using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public class AssignmentService : IAssignmentService
{
    private const int MaxOccurrences = 500;
    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly ITrainingService _training;

    public AssignmentService(IDbContextFactory<ApplicationDbContext> factory, ITrainingService training)
    {
        _factory = factory;
        _training = training;
    }

    public async Task<AssignmentValidationResult> ValidateAsync(
        string personUserId,
        int serviceSlotId,
        DateTime startUtc,
        DateTime endUtc,
        int? excludeAssignmentId = null,
        CancellationToken ct = default)
    {
        if (endUtc <= startUtc)
        {
            return AssignmentValidationResult.Fail(
                new[] { "End time must be after start time." },
                Array.Empty<string>());
        }

        await using var db = await _factory.CreateDbContextAsync(ct);

        var slot = await db.ServiceSlots
            .Include(s => s.Ministry)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serviceSlotId, ct);

        if (slot is null)
        {
            return AssignmentValidationResult.Fail(
                new[] { "Service slot not found." },
                Array.Empty<string>());
        }

        // ---- Conflict detection ---- Two ranges overlap iff:
        //     existing.Start < candidate.End AND existing.End > candidate.Start.
        var query = db.Assignments
            .Where(a => a.PersonUserId == personUserId
                && a.Status != AssignmentStatus.Cancelled
                && a.Status != AssignmentStatus.NoShow
                && a.StartUtc < endUtc
                && a.EndUtc > startUtc);

        if (excludeAssignmentId.HasValue)
            query = query.Where(a => a.Id != excludeAssignmentId.Value);

        var conflicts = await query
            .Select(a => new
            {
                a.Id,
                SlotName = a.ServiceSlot.Name,
                a.StartUtc,
                a.EndUtc,
                a.Status,
            })
            .AsNoTracking()
            .ToListAsync(ct);

        var conflictMessages = conflicts
            .Select(c => $"Conflicts with \"{c.SlotName}\" ({c.StartUtc:u} → {c.EndUtc:u}, status {c.Status})")
            .ToList();

        // ---- Training enforcement ---- Org-wide + slot-scoped requirements; row already filters
        //                              other ministries' slots out organically.
        var orgId = slot.Ministry.OrganizationId;
        var allRequiredContentIds = await db.TrainingRequirements
            .Where(r => r.OrganizationId == orgId || r.ServiceSlotId == serviceSlotId)
            .Select(r => r.TrainingContentId)
            .Distinct()
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var completions = await db.TrainingCompletions
            .Where(c => c.PersonUserId == personUserId
                && allRequiredContentIds.Contains(c.TrainingContentId))
            .ToListAsync(ct);

        var missing = new List<string>();
        foreach (var cid in allRequiredContentIds)
        {
            var c = completions
                .Where(x => x.TrainingContentId == cid)
                .OrderByDescending(x => x.CompletionUtc)
                .FirstOrDefault();
            if (c is null || !c.IsValid(now))
            {
                var title = await db.TrainingContents
                    .Where(t => t.Id == cid)
                    .Select(t => t.Title)
                    .FirstOrDefaultAsync(ct);
                missing.Add($"Training required (and currently expired): {title ?? "Unknown"}");
            }
        }

        // ---- Capacity enforcement ---- Count non-cancelled, non-no-show assignments for the
        //                              exact (slot, startUtc) pair being proposed. Capacity
        //                              comes from a matching SlotOccurrence's override if present,
        //                              else from the slot's default. If an `excludeAssignmentId`
        //                              is passed (coordinator editing), we subtract 1 so re-saving
        //                              a person's existing row doesn't false-positive on capacity.
        //                              Both manual coordinator-adds and volunteer self-signups
        //                              funnelled through AssignAsync respect this gate.
        var signedUpCount = await db.Assignments
            .Where(a => a.ServiceSlotId == serviceSlotId
                && a.StartUtc == startUtc
                && a.Status != AssignmentStatus.Cancelled
                && a.Status != AssignmentStatus.NoShow)
            .CountAsync(ct);
        if (excludeAssignmentId.HasValue && signedUpCount > 0)
        {
            signedUpCount -= await db.Assignments
                .Where(a => a.Id == excludeAssignmentId.Value)
                .CountAsync(ct);
        }
        var overrideCap = await db.SlotOccurrences
            .Where(o => o.ServiceSlotId == serviceSlotId && o.StartUtc == startUtc)
            .Select(o => (int?)o.CapacityOverride)
            .FirstOrDefaultAsync(ct);
        var capacity = overrideCap ?? slot.Capacity;
        if (signedUpCount >= capacity)
        {
            conflictMessages.Add(
                $"This shift is already full ({signedUpCount} of {capacity} signed up). " +
                "Wait for a volunteer to drop, or ask the coordinator to add capacity.");
        }

        if (conflictMessages.Count == 0 && missing.Count == 0)
        {
            return AssignmentValidationResult.Ok(new Assignment
            {
                PersonUserId = personUserId,
                ServiceSlotId = serviceSlotId,
                StartUtc = startUtc,
                EndUtc = endUtc,
            });
        }

        return AssignmentValidationResult.Fail(conflictMessages, missing);
    }

    public async Task<AssignmentValidationResult> AssignAsync(
        string personUserId,
        int serviceSlotId,
        DateTime startUtc,
        DateTime endUtc,
        string? notes = null,
        CancellationToken ct = default)
    {
        var v = await ValidateAsync(personUserId, serviceSlotId, startUtc, endUtc, null, ct);
        if (!v.Succeeded) return v;

        await using var db = await _factory.CreateDbContextAsync(ct);
        var assignment = new Assignment
        {
            PersonUserId = personUserId,
            ServiceSlotId = serviceSlotId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Status = AssignmentStatus.Scheduled,
            Notes = notes,
        };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync(ct);

        return AssignmentValidationResult.Ok(assignment);
    }

    public async Task<List<Assignment>> ListForPersonAsync(
        string personUserId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Assignments
            .Include(a => a.ServiceSlot).ThenInclude(s => s.Ministry)
            .Where(a => a.PersonUserId == personUserId
                && a.StartUtc >= fromUtc
                && a.StartUtc < toUtc
                && a.Status != AssignmentStatus.Cancelled)
            .OrderBy(a => a.StartUtc)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task<SlotOccurrenceCreationResult> CreateSlotOccurrenceAsync(
        int serviceSlotId,
        DateTime startUtc,
        DateTime endUtc,
        string? notes,
        int? capacityOverride,
        CancellationToken ct = default)
    {
        if (endUtc <= startUtc)
            return SlotOccurrenceCreationResult.Fail("End time must be after start time.");
        if (capacityOverride is int n && n <= 0)
            return SlotOccurrenceCreationResult.Fail("Capacity override must be positive.");

        await using var db = await _factory.CreateDbContextAsync(ct);
        var slot = await db.ServiceSlots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serviceSlotId, ct);
        if (slot is null)
            return SlotOccurrenceCreationResult.Fail("Service slot not found.");
        if (!slot.IsActive)
            return SlotOccurrenceCreationResult.Fail("Cannot schedule occurrences on an inactive slot.");

        var duplicate = await db.SlotOccurrences
            .AnyAsync(o => o.ServiceSlotId == serviceSlotId && o.StartUtc == startUtc, ct);
        if (duplicate)
            return SlotOccurrenceCreationResult.Fail(
                $"An open shift for this slot is already scheduled at {startUtc:u}. " +
                "Edit the existing one or pick a different time.");

        var occ = new SlotOccurrence
        {
            ServiceSlotId = serviceSlotId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            CapacityOverride = capacityOverride,
            Notes = notes,
        };
        db.SlotOccurrences.Add(occ);
        await db.SaveChangesAsync(ct);
        return SlotOccurrenceCreationResult.Ok(occ);
    }

    public async Task<List<OpenSlotOccurrenceView>> ListOpenSlotOccurrencesAsync(
        string personUserId,
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyCollection<int>? ministryIdsFilter = null,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // 1) Which orgs is this user a member of?
        var myOrgIds = await db.OrganizationMemberships
            .Where(m => m.PersonUserId == personUserId)
            .Select(m => m.OrganizationId)
            .Distinct()
            .ToListAsync(ct);

        if (myOrgIds.Count == 0) return new();

        // Active filter: only meaningful when the caller actually supplied a
        // non-empty whitelist. Null OR empty falls back to "all my orgs",
        // matching the user's "show all" toggle behavior on /Open.
        var hasFilter = ministryIdsFilter is { Count: > 0 };

        // 2) Pull every active occurrence in the window whose slot lives in one of
        //    the user's orgs (and optionally in the supplied ministry-id whitelist).
        //    We do the per-row sign-up-count and already-signed-up checks in C#
        //    below; volumes are small and the per-row query is cheap.
        var rawRows = await (
            from o in db.SlotOccurrences
            join s in db.ServiceSlots on o.ServiceSlotId equals s.Id
            join m in db.Ministries on s.MinistryId equals m.Id
            where s.IsActive
                && myOrgIds.Contains(m.OrganizationId)
                && o.StartUtc >= fromUtc
                && o.StartUtc < toUtc
                && (!hasFilter || ministryIdsFilter!.Contains(m.Id))
            orderby o.StartUtc
            select new
            {
                OccurrenceId = o.Id,
                ServiceSlotId = s.Id,
                SlotName = s.Name,
                MinistryId = m.Id,
                MinistryName = m.Name,
                OrganizationId = m.OrganizationId,
                SlotLocation = s.Location,
                StartUtc = o.StartUtc,
                EndUtc = o.EndUtc,
                CapacityOverride = o.CapacityOverride,
                SlotCapacity = s.Capacity,
                Notes = o.Notes,
            }).AsNoTracking().ToListAsync(ct);

        if (rawRows.Count == 0) return new();

        // 3) Per-row counts and already-signed-up flag bundled in one trip per row.
        var occIds = rawRows.Select(r => r.OccurrenceId).ToList();
        var slotIds = rawRows.Select(r => r.ServiceSlotId).Distinct().ToList();
        var starts = rawRows.Select(r => r.StartUtc).Distinct().ToList();

        var signups = await db.Assignments
            .Where(a => a.Status != AssignmentStatus.Cancelled
                && a.Status != AssignmentStatus.NoShow
                && slotIds.Contains(a.ServiceSlotId)
                && starts.Contains(a.StartUtc))
            .Select(a => new { a.ServiceSlotId, a.StartUtc, a.PersonUserId })
            .AsNoTracking().ToListAsync(ct);

        // 4) Training compliance across all candidate slots.
        var requiredContentIds = await db.TrainingRequirements
            .Where(r => myOrgIds.Contains(r.OrganizationId ?? -1) || slotIds.Contains(r.ServiceSlotId ?? -1))
            .Select(r => new { OrgId = r.OrganizationId ?? -1, SlotId = r.ServiceSlotId ?? -1, r.TrainingContentId })
            .AsNoTracking().ToListAsync(ct);

        var allContentIdsForUser = requiredContentIds.Select(r => r.TrainingContentId).Distinct().ToList();
        var relevantCompletions = allContentIdsForUser.Count == 0
            ? new List<TrainingCompletion>()
            : await db.TrainingCompletions
                .Where(c => c.PersonUserId == personUserId
                    && allContentIdsForUser.Contains(c.TrainingContentId))
                .AsNoTracking().ToListAsync(ct);

        var nowUtc = DateTime.UtcNow;

        var result = new List<OpenSlotOccurrenceView>();
        foreach (var r in rawRows)
        {
            var pairSignups = signups
                .Where(s => s.ServiceSlotId == r.ServiceSlotId && s.StartUtc == r.StartUtc)
                .ToList();
            var count = pairSignups.Count;
            var capacity = r.CapacityOverride ?? r.SlotCapacity;
            var alreadySignedUp = pairSignups.Any(s => s.PersonUserId == personUserId);

            // ---- Training compliance for THIS slot ----
            var requiredForRow = requiredContentIds
                .Where(x => x.OrgId == r.OrganizationId || x.SlotId == r.ServiceSlotId)
                .Select(x => x.TrainingContentId)
                .Distinct()
                .ToList();
            var missing = new List<string>();
            foreach (var cid in requiredForRow)
            {
                var latest = relevantCompletions
                    .Where(c => c.TrainingContentId == cid)
                    .OrderByDescending(c => c.CompletionUtc)
                    .FirstOrDefault();
                if (latest is null || !latest.IsValid(nowUtc))
                {
                    var title = await db.TrainingContents
                        .Where(t => t.Id == cid)
                        .Select(t => t.Title)
                        .FirstOrDefaultAsync(ct);
                    missing.Add(title ?? "Unknown training");
                }
            }

            // Hide occurrences the volunteer is already on (or that are already past).
            if (alreadySignedUp) continue;
            if (r.EndUtc <= nowUtc) continue;

            // Only show open rows: count < capacity.
            if (count >= capacity) continue;

            result.Add(new OpenSlotOccurrenceView(
                OccurrenceId: r.OccurrenceId,
                ServiceSlotId: r.ServiceSlotId,
                SlotName: r.SlotName,
                MinistryId: r.MinistryId,
                MinistryName: r.MinistryName,
                OrganizationId: r.OrganizationId,
                StartUtc: r.StartUtc,
                EndUtc: r.EndUtc,
                Location: r.SlotLocation,
                Capacity: capacity,
                SignedUpCount: count,
                AlreadySignedUp: false,
                TrainingCompliant: missing.Count == 0,
                MissingTrainings: missing));
        }
        return result;
    }

    public async Task<ScheduleSeriesResult> ScheduleSeriesAsync(
        string personUserId,
        int serviceSlotId,
        DayOfWeek dayOfWeek,
        TimeSpan localStartTime,
        int durationMinutes,
        DateTime startDate,
        DateTime endDate,
        string timeZoneId,
        CancellationToken ct = default)
    {
        if (endDate <= startDate)
            throw new ArgumentException("End date must be after start date.", nameof(endDate));
        if (durationMinutes <= 0)
            throw new ArgumentException("Duration must be positive.", nameof(durationMinutes));

        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        var created = new List<Assignment>();
        var skipped = new List<ScheduleSeriesSkipped>();
        bool capReached = false;

        DateTime firstUtc = DateTime.MaxValue;
        DateTime lastUtc = DateTime.MinValue;

        // First occurrence: first DateTime >= startDate that matches dayOfWeek.
        var cursor = startDate.Date;
        var daysUntil = ((int)dayOfWeek - (int)cursor.DayOfWeek + 7) % 7;
        cursor = cursor.AddDays(daysUntil);

        int processed = 0;
        while (cursor < endDate)
        {
            if (processed >= MaxOccurrences)
            {
                capReached = true;
                break;
            }

            // Defensive: SpecifyKind=Unspecified so ConvertTimeToUtc treats the
            // value as a wall-clock instant in `tz`, not as server-local time.
            var localDt = DateTime.SpecifyKind(cursor + localStartTime, DateTimeKind.Unspecified);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(localDt, tz);
            var endUtc = startUtc.AddMinutes(durationMinutes);

            if (startUtc < firstUtc) firstUtc = startUtc;
            if (endUtc > lastUtc) lastUtc = endUtc;

            var result = await AssignAsync(personUserId, serviceSlotId, startUtc, endUtc, ct: ct);
            if (result.Succeeded)
            {
                created.Add(result.Assignment!);
            }
            else
            {
                var reasons = new List<string>(result.Conflicts.Count + result.MissingTrainings.Count);
                reasons.AddRange(result.Conflicts);
                reasons.AddRange(result.MissingTrainings);
                skipped.Add(new ScheduleSeriesSkipped(startUtc, endUtc, reasons));
            }

            cursor = cursor.AddDays(7);
            processed++;
        }

        return new ScheduleSeriesResult(created, skipped, firstUtc, lastUtc, capReached);
    }

    /// <summary>
    /// Weekly recurrence that creates <see cref="SlotOccurrence"/> rows
    /// instead of pre-assigned <see cref="Assignment"/> rows. Mirrors
    /// <see cref="ScheduleSeriesAsync"/>'s weekly walker but the
    /// per-iteration action is "create an open shift" instead of
    /// "assign person". A pre-flight check rejects calls on non-existent
    /// or inactive slots up front so the caller sees a clear error
    /// instead of 500 silent skips.
    ///
    /// Skip reasons surfaced:
    /// - "An open shift already exists at this time" — a coordinator
    ///   pre-queued a shift at the same (slot, StartUtc) pair on a
    ///   prior run. Coordinator can delete the existing one and
    ///   re-run if they want to overwrite.
    /// </summary>
    public async Task<OpenShiftSeriesResult> ScheduleOpenShiftSeriesAsync(
        int serviceSlotId,
        DayOfWeek dayOfWeek,
        TimeSpan localStartTime,
        int durationMinutes,
        DateTime startDate,
        DateTime endDate,
        string timeZoneId,
        int? capacityOverride,
        string? notes,
        CancellationToken ct = default)
    {
        if (endDate <= startDate)
            throw new ArgumentException("End date must be after start date.", nameof(endDate));
        if (durationMinutes <= 0)
            throw new ArgumentException("Duration must be positive.", nameof(durationMinutes));
        if (capacityOverride is int n && n <= 0)
            throw new ArgumentException("Capacity override must be positive when provided.", nameof(capacityOverride));

        var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

        await using var db = await _factory.CreateDbContextAsync(ct);

        var slot = await db.ServiceSlots
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == serviceSlotId, ct);
        if (slot is null)
            throw new ArgumentException($"Service slot {serviceSlotId} not found.", nameof(serviceSlotId));
        if (!slot.IsActive)
            throw new ArgumentException("Cannot schedule a recurring series on an inactive slot.", nameof(serviceSlotId));

        var created = new List<SlotOccurrence>();
        var skipped = new List<ScheduleSeriesSkipped>();

        DateTime firstUtc = DateTime.MaxValue;
        DateTime lastUtc = DateTime.MinValue;
        bool capReached = false;

        // First occurrence: first DateTime >= startDate that matches dayOfWeek.
        var cursor = startDate.Date;
        var daysUntil = ((int)dayOfWeek - (int)cursor.DayOfWeek + 7) % 7;
        cursor = cursor.AddDays(daysUntil);

        int processed = 0;
        while (cursor < endDate)
        {
            if (processed >= MaxOccurrences)
            {
                capReached = true;
                break;
            }

            // Defensive: SpecifyKind=Unspecified so ConvertTimeToUtc treats the
            // value as a wall-clock instant in `tz`, not as server-local time.
            var localDt = DateTime.SpecifyKind(cursor + localStartTime, DateTimeKind.Unspecified);
            var startUtc = TimeZoneInfo.ConvertTimeToUtc(localDt, tz);
            var endUtc = startUtc.AddMinutes(durationMinutes);

            if (startUtc < firstUtc) firstUtc = startUtc;
            if (endUtc > lastUtc) lastUtc = endUtc;

            var duplicate = await db.SlotOccurrences
                .AnyAsync(o => o.ServiceSlotId == serviceSlotId && o.StartUtc == startUtc, ct);
            if (duplicate)
            {
                skipped.Add(new ScheduleSeriesSkipped(
                    startUtc,
                    endUtc,
                    new[] { "An open shift already exists at this time." }));
            }
            else
            {
                var occ = new SlotOccurrence
                {
                    ServiceSlotId = serviceSlotId,
                    StartUtc = startUtc,
                    EndUtc = endUtc,
                    CapacityOverride = capacityOverride,
                    Notes = notes,
                };
                db.SlotOccurrences.Add(occ);
                await db.SaveChangesAsync(ct);
                created.Add(occ);
            }

            cursor = cursor.AddDays(7);
            processed++;
        }

        return new OpenShiftSeriesResult(created, skipped, firstUtc, lastUtc, capReached);
    }
}
