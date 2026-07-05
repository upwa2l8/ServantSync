using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>Outcome of an attempt to record a training completion.</summary>
public enum TrainingCompletionResult
{
    /// <summary>The completion was inserted.</summary>
    Recorded,

    /// <summary>The TrainingContent doesn't exist (or was deleted).</summary>
    ContentNotFound,

    /// <summary>The caller is not a member of the TrainingContent's owning organization.</summary>
    NotInOrg,

    /// <summary>
    /// The caller hasn't engaged with this training enough to claim
    /// completion — for PDF, not every page was viewed; for video,
    /// not enough of the duration elapsed; for Slideshow / external
    /// URL, the dwell timer hasn't run. The Take UI surfaces this
    /// with a progress pill so the volunteer knows what's missing.
    /// </summary>
    InsufficientEngagement,
}

/// <summary>
/// Lightweight payload the Take page posts to <c>SyncActivityAsync</c>
/// as the volunteer watches / scrolls. All fields optional; the
/// activity row gets coalesced server-side with
/// <see cref="TrainingActivity.HighestWatchedSec"/> monotonic
/// <c>Math.Max</c> and viewed-pages set union so an attacker can't
/// burn down by sending smaller values.
/// </summary>
public sealed class TrainingActivitySync
{
    public int? HighestWatchedSec { get; set; }
    public int[]? ViewedPages { get; set; }
    public int? ActualDurationSec { get; set; }
}

/// <summary>
/// Snapshot the Take page reads to decide whether to expose
/// <c>Mark as completed</c>. Mirrors the eligibility rule the service
/// applies in <c>RecordCompletionAsync</c> so the page doesn't have
/// to duplicate the comparison logic.
/// </summary>
public sealed class TrainingEligibilitySnapshot
{
    public TrainingFormat Format { get; set; }
    public int TotalPages { get; set; }
    public int ViewedPagesCount { get; set; }
    public int ActualDurationSec { get; set; }
    public int HighestWatchedSec { get; set; }
    /// <summary>
    /// Seconds since the volunteer first opened the activity. For
    /// best-effort formats (Slideshow / external URL) we require a
    /// minimum dwell of 80% of the admin-entered EstimatedDuration
    /// before completion unlocks.
    /// </summary>
    public int DwellSec { get; set; }

    /// <summary>True when the server-side rule says the user can complete.</summary>
    public bool IsEligible { get; set; }

    /// <summary>Human-readable reason when <see cref="IsEligible"/> is false; empty otherwise.</summary>
    public string Reason { get; set; } = "";
}

public interface ITrainingService
{
    /// <summary>
    /// Records a completion, computing <see cref="TrainingCompletion.ExpiresUtc"/>
    /// from the requirement's cadence. The caller must be a member of
    /// <see cref="TrainingContent.Organization"/> regardless of role
    /// (TrainingContent is now per-org since round N; see STATUS.md).
    /// Returns the outcome enum instead of throwing so the page handler
    /// can surface a friendly message. The volunteer must have first
    /// engaged with the content — for PDF they have to have viewed every
    /// page; for video at least 95% of the duration; for Slideshow /
    /// external URL, at least 80% of the admin-entered EstimatedDuration
    /// dwell time. Returns <see cref="TrainingCompletionResult.InsufficientEngagement"/>
    /// when the gate fails so the page can render a progress pill.
    /// </summary>
    Task<TrainingCompletionResult> RecordCompletionAsync(
        string personUserId,
        int trainingContentId,
        DateTime completionUtc,
        string? notes = null,
        CancellationToken ct = default);

    /// <summary>
    /// Upserts the per-user <see cref="TrainingActivity"/> row for
    /// <paramref name="trainingContentId"/>. Coalesces monotonically:
    /// second counts use <c>Math.Max</c>, viewed pages use set union.
    /// The caller must be a member of the content's org — gated with
    /// the same check as <see cref="RecordCompletionAsync"/> so a
    /// malicious sync can't write into a foreign org's activity log.
    /// </summary>
    Task SyncActivityAsync(
        string personUserId,
        int trainingContentId,
        TrainingActivitySync sync,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the Take-page <see cref="TrainingEligibilitySnapshot"/>
    /// for the caller + content version. Used by the UI to show the
    /// progress pill ("12 / 18 pages, 47%") and decide whether to
    /// enable "Mark as completed".
    /// </summary>
    Task<TrainingEligibilitySnapshot> CheckEligibilityAsync(
        string personUserId,
        int trainingContentId,
        CancellationToken ct = default);

    /// <summary>Returns the list of TrainingContent requirements affecting the person that are currently invalid.</summary>
    Task<List<TrainingRequirement>> FindOutstandingRequirementsAsync(
        string personUserId,
        int organizationId,
        int? serviceSlotId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Volunteer-scoped aggregate: returns every outstanding
    /// <see cref="TrainingRequirement"/> that applies to <paramref name="personUserId"/>
    /// — union of (a) org-scoped requirements for every organization the
    /// volunteer is a member of, and (b) slot-scoped requirements for
    /// every <c>Active</c> slot they hold a non-cancelled assignment on.
    /// Excludes requirements whose TrainingContent already has a
    /// non-expired completion. Used by the "Action needed" section of
    /// the volunteer Training page.
    /// </summary>
    Task<List<TrainingRequirement>> FindMyOutstandingRequirementsAsync(
        string personUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns every <see cref="TrainingCompletion"/> the volunteer has
    /// recorded, newest first, with <see cref="TrainingContent"/>
    /// eagerly loaded so the history table can render titles + versions
    /// in a single query.
    /// </summary>
    Task<List<TrainingCompletion>> ListMyHistoryAsync(
        string personUserId,
        CancellationToken ct = default);

    /// <summary>
    /// All training-content rows owned by <paramref name="organizationId"/>,
    /// ordered by Title. Used by <c>OrgTrainingEditor.razor</c> so an
    /// Admin defining org requirements sees only their own org's catalog
    /// (no cross-org leak).
    /// </summary>
    Task<List<TrainingContent>> ListOrgTrainingAsync(
        int organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Training-content rows owned by the parent organization of
    /// <paramref name="serviceSlotId"/> (slot.Ministry.OrganizationId).
    /// Used by <c>SlotTrainingEditor.razor</c> so a Coordinator adding a
    /// slot-specific requirement picks from the slot's parent org's
    /// catalog. Empty list if the slot can't be resolved.
    /// </summary>
    Task<List<TrainingContent>> ListSlotOrgTrainingAsync(
        int serviceSlotId,
        CancellationToken ct = default);

    /// <summary>
    /// Union of training-content rows from every organization that
    /// <paramref name="adminUserId"/> is Admin of, ordered by Organization
    /// name then Title. Used by the admin "Manage catalog" page
    /// (<c>/Training/Manage</c>) so each Admin sees exactly the catalogs
    /// they own — never training belonging to an org they don't manage.
    /// </summary>
    Task<List<TrainingContent>> ListManageableTrainingAsync(
        string adminUserId,
        CancellationToken ct = default);
}
