using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>
/// Implementation of <see cref="ITrainingSessionService"/>. Every
/// mutating method gates on Admin/Coordinator of the session's
/// organization (server-side, not page-side) and validates inputs
/// before writing. Result enums flow back to the Razor pages so the
/// UI can branch with friendly messages without try/catch.
/// </summary>
public class TrainingSessionService : ITrainingSessionService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public TrainingSessionService(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<TrainingSessionMutationResult> CreateAsync(
        int organizationId,
        string title,
        string? description,
        string location,
        DateTime startUtc,
        DateTime endUtc,
        int? maxAttendees,
        int? trainingContentId,
        string callerUserId,
        CancellationToken ct = default)
    {
        // Invariant validation lives in the service layer (decision Q3)
        // matches the ServiceSlot / SlotOccurrence pattern — the model
        // trusts input, the service refuses bad input.
        var validation = ValidateInput(title, location, startUtc, endUtc, maxAttendees);
        if (validation is not null) return validation.Value;

        await using var db = await _factory.CreateDbContextAsync(ct);

        // Permission gate: caller must be Admin/Coordinator of the org.
        if (!await IsCallerOrgManagerAsync(db, callerUserId, organizationId, ct))
            return TrainingSessionMutationResult.PermissionDenied;

        // Cross-org guard on TrainingContentId — a session can't point
        // at training owned by another org (defense-in-depth even though
        // training is already per-org-scoped at the catalog level).
        if (trainingContentId.HasValue)
        {
            var contentOrgId = await db.TrainingContents
                .Where(c => c.Id == trainingContentId.Value)
                .Select(c => (int?)c.OrganizationId)
                .FirstOrDefaultAsync(ct);
            if (contentOrgId != organizationId)
                return TrainingSessionMutationResult.ValidationFailed;
        }

        var session = new TrainingSession
        {
            OrganizationId = organizationId,
            TrainingContentId = trainingContentId,
            Title = title,
            Description = description,
            Location = location,
            StartUtc = startUtc,
            EndUtc = endUtc,
            MaxAttendees = maxAttendees,
            Status = TrainingSessionStatus.Scheduled,
            CreatedByUserId = callerUserId,
        };
        db.TrainingSessions.Add(session);
        await db.SaveChangesAsync(ct);
        return TrainingSessionMutationResult.Succeeded;
    }

    public async Task<TrainingSessionMutationResult> EditAsync(
        int sessionId,
        string title,
        string? description,
        string location,
        DateTime startUtc,
        DateTime endUtc,
        int? maxAttendees,
        int? trainingContentId,
        string callerUserId,
        CancellationToken ct = default)
    {
        var validation = ValidateInput(title, location, startUtc, endUtc, maxAttendees);
        if (validation is not null) return validation.Value;

        await using var db = await _factory.CreateDbContextAsync(ct);

        var session = await db.TrainingSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return TrainingSessionMutationResult.NotFound;

        if (!await IsCallerOrgManagerAsync(db, callerUserId, session.OrganizationId, ct))
            return TrainingSessionMutationResult.PermissionDenied;

        if (session.Status == TrainingSessionStatus.Cancelled)
            return TrainingSessionMutationResult.AlreadyCancelled;
        if (session.Status == TrainingSessionStatus.Completed)
            return TrainingSessionMutationResult.AlreadyCompleted;

        if (trainingContentId.HasValue)
        {
            var contentOrgId = await db.TrainingContents
                .Where(c => c.Id == trainingContentId.Value)
                .Select(c => (int?)c.OrganizationId)
                .FirstOrDefaultAsync(ct);
            if (contentOrgId != session.OrganizationId)
                return TrainingSessionMutationResult.ValidationFailed;
        }

        // Capacity recheck: shrinking MaxAttendees below the current
        // attendee count would leave the session over-committed. Refuse
        // explicitly so a coord can't quietly push an over-filled session
        // out the door. Only enforced when the new cap is non-null AND
        // lower than the current roster (raising the cap is always fine;
        // null = "no limit" is always fine).
        if (maxAttendees.HasValue)
        {
            var currentCount = await db.TrainingSessionAttendees
                .CountAsync(a => a.TrainingSessionId == sessionId, ct);
            if (currentCount > maxAttendees.Value)
                return TrainingSessionMutationResult.ValidationFailed;
        }

        session.Title = title;
        session.Description = description;
        session.Location = location;
        session.StartUtc = startUtc;
        session.EndUtc = endUtc;
        session.MaxAttendees = maxAttendees;
        session.TrainingContentId = trainingContentId;
        await db.SaveChangesAsync(ct);
        return TrainingSessionMutationResult.Succeeded;
    }

    public async Task<TrainingSessionMutationResult> CancelAsync(
        int sessionId,
        string callerUserId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var session = await db.TrainingSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return TrainingSessionMutationResult.NotFound;

        if (!await IsCallerOrgManagerAsync(db, callerUserId, session.OrganizationId, ct))
            return TrainingSessionMutationResult.PermissionDenied;

        if (session.Status == TrainingSessionStatus.Cancelled)
            return TrainingSessionMutationResult.AlreadyCancelled;
        if (session.Status == TrainingSessionStatus.Completed)
            return TrainingSessionMutationResult.AlreadyCompleted;

        session.Status = TrainingSessionStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        return TrainingSessionMutationResult.Succeeded;
    }

    public async Task<List<TrainingSession>> ListUpcomingAsync(
        int organizationId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TrainingSessions
            .Include(s => s.TrainingContent)
            .Include(s => s.Attendees)
            .AsNoTracking()
            .Where(s => s.OrganizationId == organizationId
                && s.Status == TrainingSessionStatus.Scheduled
                && s.StartUtc >= DateTime.UtcNow
                && s.StartUtc < DateTime.UtcNow.AddDays(60))
            .OrderBy(s => s.StartUtc)
            .ToListAsync(ct);
    }

    public async Task<List<TrainingSession>> ListPastAsync(
        int organizationId,
        DateTime sinceUtc,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TrainingSessions
            .Include(s => s.TrainingContent)
            .Include(s => s.Attendees)
            .AsNoTracking()
            .Where(s => s.OrganizationId == organizationId
                && s.StartUtc >= sinceUtc
                && (s.Status == TrainingSessionStatus.Completed
                    || s.Status == TrainingSessionStatus.Cancelled
                    || s.EndUtc < DateTime.UtcNow))
            .OrderByDescending(s => s.StartUtc)
            .ToListAsync(ct);
    }

    public async Task<TrainingSession?> GetAsync(
        int sessionId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TrainingSessions
            .Include(s => s.TrainingContent)
            .Include(s => s.Attendees)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
    }

    public async Task<TrainingSessionSignupResult> SignUpAsync(
        int sessionId,
        string personUserId,
        string callerUserId,
        CancellationToken ct = default)
    {
        // IDOR defense: a page handler (or a malicious form) can't sign
        // up a different user under their own account. The service is
        // the security boundary (matches MemberManagementService etc.),
        // so we enforce caller==person here. An empty callerUserId is
        // also refused — a page that forgot to pass the param (or was
        // compromised) must not silently sign up strangers. Return
        // NotFound (NOT PermissionDenied) to keep the same "don't leak
        // existence" posture as the rest of the volunteer self-service
        // surface.
        if (string.IsNullOrEmpty(callerUserId) || callerUserId != personUserId)
            return TrainingSessionSignupResult.NotFound;

        await using var db = await _factory.CreateDbContextAsync(ct);

        var session = await db.TrainingSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return TrainingSessionSignupResult.NotFound;

        // The volunteer must be a member of the session's org. Outsiders
        // get NotFound (not a PermissionDenied leak) so we don't reveal
        // session id existence to non-members.
        var inOrg = await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == personUserId
                && m.OrganizationId == session.OrganizationId, ct);
        if (!inOrg) return TrainingSessionSignupResult.NotFound;

        if (session.Status == TrainingSessionStatus.Cancelled)
            return TrainingSessionSignupResult.SessionCancelled;

        // The composite-unique index on (TrainingSessionId, PersonUserId)
        // catches double-signup at the DB level, but we check in C# first
        // so the page gets a clean AlreadySignedUp enum value.
        var alreadySignedUp = await db.TrainingSessionAttendees
            .AnyAsync(a => a.TrainingSessionId == sessionId
                && a.PersonUserId == personUserId, ct);
        if (alreadySignedUp) return TrainingSessionSignupResult.AlreadySignedUp;

        // Decision Q1: ENFORCE capacity. Refuse with SessionFull when
        // the count is already at MaxAttendees.
        if (session.MaxAttendees.HasValue)
        {
            var currentCount = await db.TrainingSessionAttendees
                .CountAsync(a => a.TrainingSessionId == sessionId, ct);
            if (currentCount >= session.MaxAttendees.Value)
                return TrainingSessionSignupResult.SessionFull;
        }

        db.TrainingSessionAttendees.Add(new TrainingSessionAttendee
        {
            TrainingSessionId = sessionId,
            PersonUserId = personUserId,
        });
        await db.SaveChangesAsync(ct);
        return TrainingSessionSignupResult.SignedUp;
    }

    public async Task<TrainingSessionSignupResult> CancelSignUpAsync(
        int sessionId,
        string personUserId,
        string callerUserId,
        CancellationToken ct = default)
    {
        // IDOR defense (matches SignUpAsync): the volunteer is the only
        // one allowed to cancel their own sign-up. A page can't cancel
        // someone else's. An empty callerUserId is also refused so a
        // page that forgot to pass the param can't silently cancel a
        // stranger's sign-up.
        if (string.IsNullOrEmpty(callerUserId) || callerUserId != personUserId)
            return TrainingSessionSignupResult.NotFound;

        await using var db = await _factory.CreateDbContextAsync(ct);

        var session = await db.TrainingSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return TrainingSessionSignupResult.NotFound;

        // A cancellation on a Cancelled session is a no-op attempt from
        // the volunteer's POV. Return SessionCancelled so the page can
        // show "session was cancelled, no further action".
        if (session.Status == TrainingSessionStatus.Cancelled)
            return TrainingSessionSignupResult.SessionCancelled;

        var attendee = await db.TrainingSessionAttendees
            .FirstOrDefaultAsync(a => a.TrainingSessionId == sessionId
                && a.PersonUserId == personUserId, ct);
        if (attendee is null) return TrainingSessionSignupResult.NotSignedUp;

        // Per PLAN edge case: a volunteer who has been marked attended
        // can't self-cancel — the marker is the audit-trail owner. Admin
        // must mediate via a follow-up flow (not in round 1).
        if (attendee.Attended == true)
            return TrainingSessionSignupResult.AlreadyMarkedAttended;

        db.TrainingSessionAttendees.Remove(attendee);
        await db.SaveChangesAsync(ct);
        return TrainingSessionSignupResult.Cancelled;
    }

    public async Task<TrainingSessionMutationResult> MarkAttendeesCompleteAsync(
        int sessionId,
        string markerUserId,
        IReadOnlyList<AttendeeMark> attendeeResults,
        string markerNotes,
        CancellationToken ct = default)
    {
        if (attendeeResults is null || attendeeResults.Count == 0)
            return TrainingSessionMutationResult.NoAttendees;
        // Decision Q5: notes REQUIRED on the bulk mark path (mirrors
        // ITrainingService.MarkSingleCompleteAsync's policy).
        if (string.IsNullOrWhiteSpace(markerNotes))
            return TrainingSessionMutationResult.ValidationFailed;

        await using var db = await _factory.CreateDbContextAsync(ct);

        var session = await db.TrainingSessions
            .FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null) return TrainingSessionMutationResult.NotFound;

        if (!await IsCallerOrgManagerAsync(db, markerUserId, session.OrganizationId, ct))
            return TrainingSessionMutationResult.PermissionDenied;

        // Defense in depth: every marked PersonUserId must be an org
        // member. A marker can't forge training records for strangers,
        // and a non-volunteer walk-in should be added to the org by the
        // coord before being marked attended (admin follow-up flow if
        // they refuse). Count-mismatch refuses with ValidationFailed so
        // the page can surface "X is not a member of this org".
        var markedIds = attendeeResults.Select(r => r.PersonUserId).Distinct().ToList();
        var memberCount = await db.OrganizationMemberships
            .Where(m => m.OrganizationId == session.OrganizationId
                && markedIds.Contains(m.PersonUserId))
            .CountAsync(ct);
        if (memberCount != markedIds.Count)
            return TrainingSessionMutationResult.ValidationFailed;

        // Pre-load every attendee row we'll mutate in ONE query, then
        // dictionary-lookup in the loop. Pre-fix this was N+1 (one
        // FirstOrDefaultAsync per attendee); for a 50-volunteer roster
        // the previous shape burned 50 round-trips on every mark call.
        var existingAttendees = await db.TrainingSessionAttendees
            .Where(a => a.TrainingSessionId == sessionId
                && markedIds.Contains(a.PersonUserId))
            .ToDictionaryAsync(a => a.PersonUserId, ct);

        // Resolve the content version + ExpiresUtc once for the whole
        // call. Skipped entirely when the session has no TrainingContentId
        // (free-form "general orientation" session — only the Attended
        // flag is recorded; no TrainingCompletion rows).
        int? resolvedContentVersion = null;
        DateTime? expiresUtc = null;
        if (session.TrainingContentId.HasValue)
        {
            var content = await db.TrainingContents
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == session.TrainingContentId.Value, ct);
            if (content is not null)
            {
                resolvedContentVersion = content.Version;
                // Same cadence derivation as RecordCompletionAsync /
                // MarkSingleCompleteAsync so the session-attended mark
                // carries identical expiry semantics.
                var req = await db.TrainingRequirements
                    .Where(r => r.TrainingContentId == session.TrainingContentId.Value)
                    .OrderBy(r => r.Id)
                    .FirstOrDefaultAsync(ct);
                var nowUtc = DateTime.UtcNow;
                expiresUtc = req?.Cadence switch
                {
                    TrainingCadence.OneTime => null,
                    TrainingCadence.Yearly => nowUtc.AddYears(1),
                    TrainingCadence.EveryMonths => nowUtc.AddMonths(req.CadenceMonths ?? 12),
                    _ => nowUtc.AddYears(1),
                };
            }
        }

        foreach (var markInput in attendeeResults)
        {
            TrainingSessionAttendee attendee;
            if (existingAttendees.TryGetValue(markInput.PersonUserId, out var existing))
            {
                // Decision Q7: latest-wins — the Attended flag is upserted.
                existing.Attended = markInput.Attended;
                attendee = existing;
            }
            else
            {
                // Marker can include a volunteer who wasn't on the
                // original roster — they showed up after the fact. We
                // auto-add them and set Attended in a single insert so
                // the verification list doesn't lose the audit trail.
                attendee = new TrainingSessionAttendee
                {
                    TrainingSessionId = sessionId,
                    PersonUserId = markInput.PersonUserId,
                    Attended = markInput.Attended,
                };
                db.TrainingSessionAttendees.Add(attendee);
                // Add to the local cache too in case the same person
                // appears twice in the input (would be a UI bug; we
                // don't want to double-insert and trip the unique
                // index at SaveChanges).
                existingAttendees[markInput.PersonUserId] = attendee;
            }

            // TrainingCompletion only when attended=true AND session has
            // training content AND we resolved the version (a content row
            // could vanish if the SetNull behavior caught a deletion race;
            // we'd rather record attendance on the attendee row than fail
            // the whole mark call).
            if (markInput.Attended
                && session.TrainingContentId.HasValue
                && resolvedContentVersion.HasValue)
            {
                UpsertCompletion(
                    db, markInput.PersonUserId, session.TrainingContentId.Value,
                    resolvedContentVersion.Value, markerUserId, markerNotes, expiresUtc);
            }
        }

        // Flip session status once attendance is finalized. If the
        // session is already Cancelled we leave status alone — the spec
        // allows marking attendance for cancelled sessions (audit trail
        // survives cancellation), but the session itself stays Cancelled.
        if (session.Status == TrainingSessionStatus.Scheduled)
            session.Status = TrainingSessionStatus.Completed;
        await db.SaveChangesAsync(ct);
        return TrainingSessionMutationResult.Succeeded;
    }

    // ---- Shared helpers ----

    private static void UpsertCompletion(
        ApplicationDbContext db,
        string personUserId,
        int contentId,
        int contentVersion,
        string markerUserId,
        string markerNotes,
        DateTime? expiresUtc)
    {
        var existing = db.TrainingCompletions.FirstOrDefault(c =>
            c.PersonUserId == personUserId
            && c.TrainingContentId == contentId
            && c.TrainingContentVersion == contentVersion);
        if (existing is not null)
        {
            // Latest-wins (decision Q7). Marker + notes + source + utc +
            // expires are overwritten in place — the audit trail captures
            // who made the most recent mark AND what they wrote.
            existing.CompletionSource = TrainingCompletionSource.CoordinatorManual;
            existing.MarkedCompleteByUserId = markerUserId;
            existing.ManualCompletionNotes = markerNotes;
            existing.CompletionUtc = DateTime.UtcNow;
            existing.ExpiresUtc = expiresUtc;
        }
        else
        {
            db.TrainingCompletions.Add(new TrainingCompletion
            {
                PersonUserId = personUserId,
                TrainingContentId = contentId,
                TrainingContentVersion = contentVersion,
                CompletionUtc = DateTime.UtcNow,
                ExpiresUtc = expiresUtc,
                CompletionSource = TrainingCompletionSource.CoordinatorManual,
                MarkedCompleteByUserId = markerUserId,
                ManualCompletionNotes = markerNotes,
            });
        }
    }

    private static TrainingSessionMutationResult? ValidateInput(
        string title,
        string location,
        DateTime startUtc,
        DateTime endUtc,
        int? maxAttendees)
    {
        if (string.IsNullOrWhiteSpace(title)) return TrainingSessionMutationResult.ValidationFailed;
        if (string.IsNullOrWhiteSpace(location)) return TrainingSessionMutationResult.ValidationFailed;
        if (endUtc <= startUtc) return TrainingSessionMutationResult.ValidationFailed;
        if (maxAttendees.HasValue && maxAttendees.Value <= 0) return TrainingSessionMutationResult.ValidationFailed;
        return null;
    }

    private static async Task<bool> IsCallerOrgManagerAsync(
        ApplicationDbContext db,
        string userId,
        int orgId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(userId)) return false;
        var role = await db.OrganizationMemberships
            .Where(m => m.PersonUserId == userId && m.OrganizationId == orgId)
            .Select(m => (OrganizationRole?)m.Role)
            .FirstOrDefaultAsync(ct);
        return role == OrganizationRole.Admin || role == OrganizationRole.Coordinator;
    }
}
