using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public class TrainingService : ITrainingService
{
    private readonly IDbContextFactory<ApplicationDbContext> _factory;

    public TrainingService(IDbContextFactory<ApplicationDbContext> factory)
    {
        _factory = factory;
    }        // Eligibility thresholds. Pull out so AdjustIfFewerPages / future
    // A/B tests can tweak them without touching the rule body.
    private const double StrictVideoRatio = 0.95;     // of actual duration
    private const double BestEffortDwellRatio = 0.80; // of admin-entered EstimatedDuration

    // Anti-cheat floors — a hostile client can't forge engagement with a
    // single 1-second sync. ActualDurationSec below this is treated as
    // "duration not yet detected" so the rule refuses to unlock. Highest
    // across all formats gets a 30-second absolute floor so even very
    // short videos/Slideshows can't be burned down to 1s.
    private const int MinActualDurationSec = 10;
    private const int MinAbsoluteDwellSec = 30;
    // A volunteer who already has an activity row and comes back to the
    // Take page after this much idle time gets a fresh dwell window —
    // otherwise FirstOpenedUtc would never reset and you'd qualify for a
    // 60s Slideshow only by opening the page once and coming back a week
    // later.
    private static readonly TimeSpan SessionResetWindow = TimeSpan.FromMinutes(30);

    public async Task<TrainingCompletionResult> RecordCompletionAsync(
        string personUserId,
        int trainingContentId,
        DateTime completionUtc,
        string? notes = null,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        // Pull the content row together with its owning org id so we can
        // gate in a single round-trip. EF's fix-up exposes the FK value via
        // the foreign-key property even before the .Include's reference
        // property is materialized.
        var content = await db.TrainingContents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == trainingContentId, ct);
        if (content is null) return TrainingCompletionResult.ContentNotFound;

        // Org-membership gate: training is per-org since round N, and a
        // completion row is only meaningful for someone in the org that
        // owns the content. Mirrors the gate applied at Take.razor so the
        // service is the security boundary (page handlers stay thin). We
        // use the same OrganizationMembership check the rest of the app
        // uses — Volunteer, Coordinator, and Admin all carry it equally.
        var callerInOrg = await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == personUserId
                && m.OrganizationId == content.OrganizationId, ct);
        if (!callerInOrg) return TrainingCompletionResult.NotInOrg;

        // Engagement gate (round M): only call this far for someone who
        // has already been on the content long enough to qualify. The
        // snapshot uses the same rule the Take page renders so a hostile
        // client can't skip the check by hitting RecordCompletionAsync
        // directly — this is the trust boundary.
        var eligibility = await CheckEligibilityAsync(personUserId, trainingContentId, ct);
        if (!eligibility.IsEligible) return TrainingCompletionResult.InsufficientEngagement;

        // Find any active requirement this completion satisfies. Resolve
        // the cadence from the requirement first (matches the old
        // pre-round-N behavior — single-org requirement lookup is the
        // canonical way to derive expiry); fall back to Yearly if no
        // requirement references the content yet (admins sometimes add
        // training before wiring it into a requirement).
        var req = await db.TrainingRequirements
            .Where(r => r.TrainingContentId == trainingContentId)
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(ct);

        DateTime? expiresUtc = req?.Cadence switch
        {
            TrainingCadence.OneTime => null,
            TrainingCadence.Yearly => completionUtc.AddYears(1),
            TrainingCadence.EveryMonths => completionUtc.AddMonths(req.CadenceMonths ?? 12),
            _ => completionUtc.AddYears(1),
        };

        var completion = new TrainingCompletion
        {
            PersonUserId = personUserId,
            TrainingContentId = trainingContentId,
            TrainingContentVersion = content.Version,
            CompletionUtc = completionUtc,
            ExpiresUtc = expiresUtc,
            Notes = notes,
            // Round-FR-2: explicit default-zero source on the
            // engagement-verified path even though it's the enum's
            // default — pinning here makes the round-AV-and-prior
            // baseline behavior obvious to future readers and protects
            // against a future enum reorder moving UserOnline off zero.
            CompletionSource = TrainingCompletionSource.UserOnline,
        };
        db.TrainingCompletions.Add(completion);
        await db.SaveChangesAsync(ct);
        return TrainingCompletionResult.Recorded;
    }

    public async Task SyncActivityAsync(
        string personUserId,
        int trainingContentId,
        TrainingActivitySync sync,
        CancellationToken ct = default)
    {
        if (sync is null) throw new ArgumentNullException(nameof(sync));

        await using var db = await _factory.CreateDbContextAsync(ct);

        // Org-membership gate first — mirror the RecordCompletionAsync
        // pattern so a hostile client can't write into a foreign org's
        // activity log to later spoof a completion somewhere else.
        var content = await db.TrainingContents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == trainingContentId, ct);
        if (content is null) return; // silently — the client will see contentNotFound on completion

        var callerInOrg = await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == personUserId
                && m.OrganizationId == content.OrganizationId, ct);
        if (!callerInOrg) return;

        var now = DateTime.UtcNow;
        var activity = await db.TrainingActivities
            .FirstOrDefaultAsync(a => a.PersonUserId == personUserId
                && a.TrainingContentId == trainingContentId
                && a.TrainingContentVersion == content.Version, ct);

        var coalescedPages = activity?.GetViewedPages() ?? new HashSet<int>();
        if (sync.ViewedPages is not null)
        {
            // Defensive cap so a malicious payload can't blow up the row.
            // Real engagement per session tops out around a few hundred
            // distinct pages — a big cap here is purely a sanity ceiling.
            var cap = Math.Min(sync.ViewedPages.Length, 5000);
            for (var i = 0; i < cap; i++)
            {
                var p = sync.ViewedPages[i];
                if (p > 0) coalescedPages.Add(p);
            }
        }

        if (activity is null)
        {
            activity = new TrainingActivity
            {
                PersonUserId = personUserId,
                TrainingContentId = trainingContentId,
                TrainingContentVersion = content.Version,
                FirstOpenedUtc = now,
                LastUpdatedUtc = now,
            };
            db.TrainingActivities.Add(activity);
        }
        else
        {
            // Session-reset: if the volunteer has been idle longer than
            // SessionResetWindow, the new visit counts as a fresh dwell
            // window. Without this check the dwell calc keeps ticking
            // from FirstOpenedUtc across days of idle, letting a user
            // qualify by simply opening the training once.
            if (now - activity.LastUpdatedUtc > SessionResetWindow)
            {
                activity.FirstOpenedUtc = now;
            }
            activity.LastUpdatedUtc = now;
        }

        // HighestWatchedSec is monotonic — never trust the client to
        // burn it down, just take the max of what we had vs what they
        // sent. Same invariant applies to ActualDurationSec, which is
        // the JS-reported media length.
        if (sync.HighestWatchedSec is > 0)
            activity.HighestWatchedSec = Math.Max(activity.HighestWatchedSec, sync.HighestWatchedSec.Value);
        if (sync.ActualDurationSec is > 0)
            activity.ActualDurationSec = Math.Max(activity.ActualDurationSec, sync.ActualDurationSec.Value);

        activity.ViewedPagesCsv = TrainingActivity.SerializeViewedPages(coalescedPages);
        await db.SaveChangesAsync(ct);
    }

    public async Task<TrainingEligibilitySnapshot> CheckEligibilityAsync(
        string personUserId,
        int trainingContentId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var content = await db.TrainingContents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == trainingContentId, ct);
        if (content is null)
        {
            return new TrainingEligibilitySnapshot { Reason = "Training not found." };
        }

        // Members of the content's org always pass — encourages admins
        // and coordinators to verify their own training works.
        var callerInOrg = await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == personUserId
                && m.OrganizationId == content.OrganizationId, ct);
        if (!callerInOrg)
        {
            return new TrainingEligibilitySnapshot
            {
                Format = content.Format,
                Reason = "You're not a member of this training's organization.",
            };
        }

        var activity = await db.TrainingActivities
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.PersonUserId == personUserId
                && a.TrainingContentId == trainingContentId
                && a.TrainingContentVersion == content.Version, ct);

        var viewed = activity?.GetViewedPages() ?? new HashSet<int>();
        var dwellSec = activity is null ? 0 : (int)Math.Max(0, (DateTime.UtcNow - activity.FirstOpenedUtc).TotalSeconds);

        // Two effective total-pages denominators: server-stored
        // TotalPageCount for uploaded PDFs (computed on upload via
        // PdfPageCounter); null for non-PDF formats so the rule falls
        // through to whichever lower-strictness rule fits.
        var totalPages = content.TotalPageCount ?? 0;
        var actualDuration = activity?.ActualDurationSec ?? 0;
        var highestSec = activity?.HighestWatchedSec ?? 0;

        var snapshot = new TrainingEligibilitySnapshot
        {
            Format = content.Format,
            TotalPages = totalPages,
            ViewedPagesCount = viewed.Count,
            ActualDurationSec = actualDuration,
            HighestWatchedSec = highestSec,
            DwellSec = dwellSec,
        };

        // Per-format rule.
        switch (content.Format)
        {
            case TrainingFormat.Pdf:
                // Strict "every page viewed" gate. Defensive: if the
                // server-side page count is missing, fall back to
                // "no rule" — the page won't show "Mark complete" but
                // will also fail loudly instead of silently approving.
                if (totalPages <= 0)
                {
                    snapshot.IsEligible = false;
                    snapshot.Reason = "PDF page count unknown — admin re-upload required.";
                    return snapshot;
                }
                if (viewed.Count < totalPages)
                {
                    snapshot.IsEligible = false;
                    snapshot.Reason = $"Viewed {viewed.Count} of {totalPages} pages.";
                    return snapshot;
                }
                snapshot.IsEligible = true;
                return snapshot;

            case TrainingFormat.Video:
                // Anti-cheat floor: a hostile client forging a 1s
                // ActualDurationSec + 1s HighestWatchedSec would
                // otherwise pass the ratio rule trivially. We refuse
                // anything below MinActualDurationSec — the JS bridge
                // sends the real `<video>.duration` value after
                // loadedmetadata; if the value is missing or near-zero
                // the user hasn't actually watched enough of the
                // resource for the player to have loaded.
                if (actualDuration < MinActualDurationSec)
                {
                    snapshot.IsEligible = false;
                    snapshot.Reason = "Video duration not yet detected.";
                    return snapshot;
                }
                if (highestSec < actualDuration * StrictVideoRatio)
                {
                    snapshot.IsEligible = false;
                    var pct = (int)Math.Floor(100.0 * highestSec / actualDuration);
                    snapshot.Reason = $"Watched {pct}% — finish at least {Math.Ceiling(StrictVideoRatio * 100)}% to complete.";
                    return snapshot;
                }
                // Absolute-floor check: even with a 10s video played
                // twice, the volunteer hasn't accumulated a meaningful
                // engagement shape. Skip the floor for genuinely long
                // videos (>300s) where StrictVideoRatio is already a
                // stronger gate.
                if (highestSec < MinAbsoluteDwellSec && actualDuration <= 300)
                {
                    snapshot.IsEligible = false;
                    snapshot.Reason = $"Watched only {highestSec}s — finish at least {MinAbsoluteDwellSec}s to complete.";
                    return snapshot;
                }
                snapshot.IsEligible = true;
                return snapshot;

            case TrainingFormat.Slideshow:
            default:
                // Best-effort: dwell timer against the admin-entered
                // EstimatedDuration. Falling back to 30s if missing so a
                // zero-duration row doesn't auto-qualify.
                var requiredSec = (int)Math.Max(
                    (content.EstimatedDuration ?? TimeSpan.FromSeconds(30)).TotalSeconds * BestEffortDwellRatio,
                    MinAbsoluteDwellSec);
                if (dwellSec < requiredSec)
                {
                    snapshot.IsEligible = false;
                    snapshot.Reason = $"Best-effort timer: {dwellSec}s of {requiredSec}s elapsed.";
                    return snapshot;
                }
                snapshot.IsEligible = true;
                return snapshot;
        }
    }

    public async Task<List<TrainingRequirement>> FindOutstandingRequirementsAsync(
        string personUserId,
        int organizationId,
        int? serviceSlotId = null,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var reqIds = await db.TrainingRequirements
            .Where(r => r.OrganizationId == organizationId
                || (r.ServiceSlotId != null && serviceSlotId.HasValue && r.ServiceSlotId == serviceSlotId))
            .Select(r => r.Id)
            .ToListAsync(ct);

        if (reqIds.Count == 0) return new List<TrainingRequirement>();

        var validContentIds = await db.TrainingCompletions
            .Where(c => c.PersonUserId == personUserId
                && c.ExpiresUtc != null
                && c.ExpiresUtc > now)
            .Select(c => c.TrainingContentId)
            .ToListAsync(ct);

        var outstanding = await db.TrainingRequirements
            .Include(r => r.TrainingContent)
            .Where(r => reqIds.Contains(r.Id)
                && !validContentIds.Contains(r.TrainingContentId))
            .AsNoTracking()
            .ToListAsync(ct);

        return outstanding;
    }

    public async Task<List<TrainingRequirement>> FindMyOutstandingRequirementsAsync(
        string personUserId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;

        var myOrgIds = await db.OrganizationMemberships
            .Where(m => m.PersonUserId == personUserId)
            .Select(m => m.OrganizationId)
            .ToListAsync(ct);

        var mySlotIds = await db.Assignments
            .Where(a => a.PersonUserId == personUserId
                && a.Status != AssignmentStatus.Cancelled)
            .Select(a => a.ServiceSlotId)
            .ToListAsync(ct);

        if (myOrgIds.Count == 0 && mySlotIds.Count == 0)
        {
            return new List<TrainingRequirement>();
        }

        var validContentIds = await db.TrainingCompletions
            .Where(c => c.PersonUserId == personUserId
                && c.ExpiresUtc != null
                && c.ExpiresUtc > now)
            .Select(c => c.TrainingContentId)
            .ToListAsync(ct);

        return await db.TrainingRequirements
            .Include(r => r.TrainingContent)
            .AsNoTracking()
            .Where(r => !validContentIds.Contains(r.TrainingContentId)
                && ((myOrgIds.Count > 0 && r.OrganizationId != null && myOrgIds.Contains(r.OrganizationId.Value))
                    || (mySlotIds.Count > 0 && r.ServiceSlotId != null && mySlotIds.Contains(r.ServiceSlotId.Value))))
            .ToListAsync(ct);
    }

    public async Task<List<TrainingCompletion>> ListMyHistoryAsync(
        string personUserId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TrainingCompletions
            .Include(c => c.TrainingContent)
            .AsNoTracking()
            .Where(c => c.PersonUserId == personUserId)
            .OrderByDescending(c => c.CompletionUtc)
            .ToListAsync(ct);
    }

    public async Task<List<TrainingContent>> ListOrgTrainingAsync(
        int organizationId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.TrainingContents
            .AsNoTracking()
            .Where(c => c.OrganizationId == organizationId)
            .OrderBy(c => c.Title)
            .ToListAsync(ct);
    }

    public async Task<List<TrainingContent>> ListSlotOrgTrainingAsync(
        int serviceSlotId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Resolve the slot → ministry → org chain so the dropdown shows
        // only the parent org's catalog. If the slot doesn't exist
        // (deleted, etc.), return an empty list rather than throwing —
        // the SlotTrainingEditor UI shows its own "no training available"
        // empty state in that case.
        var orgId = await db.ServiceSlots
            .Where(s => s.Id == serviceSlotId)
            .Select(s => (int?)s.Ministry.OrganizationId)
            .FirstOrDefaultAsync(ct);
        if (orgId is null) return new List<TrainingContent>();
        return await ListOrgTrainingAsync(orgId.Value, ct);
    }

    public async Task<List<TrainingContent>> ListManageableTrainingAsync(
        string adminUserId,
        CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Union of training from every org the caller is Admin of. The
        // role-gate table is OrganizationMembership with Role=Admin;
        // The Int values are the org ids; we then join to TrainingContents
        // by OrganizationId and eager-load Organization so the page
        // header can render "Admin of: X, Y" without an N+1 trip.
        return await db.TrainingContents
            .Include(c => c.Organization)
            .AsNoTracking()
            .Where(c => db.OrganizationMemberships
                .Any(m => m.PersonUserId == adminUserId
                    && m.Role == OrganizationRole.Admin
                    && m.OrganizationId == c.OrganizationId))
            .OrderBy(c => c.Organization!.Name)
            .ThenBy(c => c.Title)
            .ToListAsync(ct);
    }

    // -----------------------------------------------------------------
    // Round-FR-2.2: MarkSingleCompleteAsync — coordinator/admin
    // ad-hoc manual mark without an in-person session. Bypasses the
    // engagement-eligibility gate entirely (decision Q6): the marker
    // asserts out-of-band competence. Notes REQUIRED (decision Q5).
    // -----------------------------------------------------------------
    public async Task<TrainingCompletionResult> MarkSingleCompleteAsync(
        int trainingContentId,
        string personUserId,
        string markerUserId,
        string markerNotes,
        CancellationToken ct = default)
    {
        // Decision Q5: notes REQUIRED on every manual mark.
        if (string.IsNullOrWhiteSpace(markerNotes))
            return TrainingCompletionResult.ManualMarkNotesRequired;

        await using var db = await _factory.CreateDbContextAsync(ct);

        var content = await db.TrainingContents
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == trainingContentId, ct);
        if (content is null) return TrainingCompletionResult.ContentNotFound;

        // Permission gate: caller must be Admin/Coordinator of the
        // content's org. Mirrors the audit story — a marker asserts
        // competence out of band, so the gate is "this caller has the
        // authority to assert competence for this org's training".
        var callerRole = await db.OrganizationMemberships
            .Where(m => m.PersonUserId == markerUserId && m.OrganizationId == content.OrganizationId)
            .Select(m => (OrganizationRole?)m.Role)
            .FirstOrDefaultAsync(ct);
        // Round-FR-5: Admin OR MinistryDirector of the content's
        // org. Slot Coordinators deliberately NOT included — manual
        // completion marks are a training-management concern, not a
        // slot concern.
        if (callerRole != OrganizationRole.Admin && callerRole != OrganizationRole.MinistryDirector)
            return TrainingCompletionResult.ManualMarkPermissionDenied;

        // The volunteer being marked must be a member of the content's
        // org — otherwise we'd be writing a training record for a
        // stranger who never joined. Mirrors RecordCompletionAsync's
        // NotInOrg branch.
        var volunteerInOrg = await db.OrganizationMemberships
            .AnyAsync(m => m.PersonUserId == personUserId && m.OrganizationId == content.OrganizationId, ct);
        if (!volunteerInOrg) return TrainingCompletionResult.NotInOrg;

        // Cadence-derived expiry (matches RecordCompletionAsync). The
        // Yearly fallback executes if no TrainingRequirement references
        // the content — admins sometimes add training before wiring
        // it into a requirement; we still want expiration-aware
        // completion semantics.
        var req = await db.TrainingRequirements
            .Where(r => r.TrainingContentId == trainingContentId)
            .OrderBy(r => r.Id)
            .FirstOrDefaultAsync(ct);
        var nowUtc = DateTime.UtcNow;
        DateTime? expiresUtc = req?.Cadence switch
        {
            TrainingCadence.OneTime => null,
            TrainingCadence.Yearly => nowUtc.AddYears(1),
            TrainingCadence.EveryMonths => nowUtc.AddMonths(req.CadenceMonths ?? 12),
            _ => nowUtc.AddYears(1),
        };

        // Decision Q7: latest-wins. Upsert in place so a second manual
        // mark overwrites the first one's source/marker/notes/utc/expires
        // — the audit trail captures WHO marked most recently AND what
        // they wrote. A separate TrainingCompletionAudit table is a
        // future-round opportunity if multi-mark history becomes a real
        // need.
        var existing = await db.TrainingCompletions
            .FirstOrDefaultAsync(c => c.PersonUserId == personUserId
                && c.TrainingContentId == trainingContentId
                && c.TrainingContentVersion == content.Version, ct);

        if (existing is not null)
        {
            existing.CompletionSource = TrainingCompletionSource.CoordinatorManualSingle;
            existing.MarkedCompleteByUserId = markerUserId;
            existing.ManualCompletionNotes = markerNotes;
            existing.CompletionUtc = nowUtc;
            existing.ExpiresUtc = expiresUtc;
        }
        else
        {
            db.TrainingCompletions.Add(new TrainingCompletion
            {
                PersonUserId = personUserId,
                TrainingContentId = trainingContentId,
                TrainingContentVersion = content.Version,
                CompletionUtc = nowUtc,
                ExpiresUtc = expiresUtc,
                CompletionSource = TrainingCompletionSource.CoordinatorManualSingle,
                MarkedCompleteByUserId = markerUserId,
                ManualCompletionNotes = markerNotes,
            });
        }

        await db.SaveChangesAsync(ct);
        return TrainingCompletionResult.ManualMarkRecorded;
    }
}
