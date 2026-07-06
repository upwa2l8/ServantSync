using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>
/// Outcome of a coordinator/admin write to a <see cref="TrainingSession"/>.
/// Result-enum shape matches the codebase's MemberManagementService /
/// ArenaService / CoordinatorAssignmentsService patterns — pages branch
/// without exceptions so UI surface messages are friendly and testable.
/// </summary>
public enum TrainingSessionMutationResult
{
    /// <summary>The write succeeded.</summary>
    Succeeded,

    /// <summary>The caller isn't Admin/Coordinator of the session's org.</summary>
    PermissionDenied,

    /// <summary>The session id doesn't exist (already deleted, wrong route).</summary>
    NotFound,

    /// <summary>
    /// The input failed validation: empty title/location, EndUtc &lt;=
    /// StartUtc, MaxAttendees &lt;= 0, MaxAttendees shrunk below the
    /// current attendee count, or a trainingContentId that doesn't
    /// belong to the same org.
    /// </summary>
    ValidationFailed,

    /// <summary>The session is already Cancelled; can't edit/cancel a second time.</summary>
    AlreadyCancelled,

    /// <summary>The session is Completed; can't edit/cancel; marker has finalized attendance.</summary>
    AlreadyCompleted,

    /// <summary>MarkAttendeesCompleteAsync was called with an empty attendee list.</summary>
    NoAttendees,
}

/// <summary>
/// Outcome of a volunteer's sign-up / cancel-sign-up action on a
/// <see cref="TrainingSession"/>. Sign-up is volunteer self-service
/// (any org member), distinct from the coord/admin mutation surface
/// above. <see cref="TrainingSessionMutationResult"/> is repurposed-unsafe
/// here because the success / failure shapes diverge (cancellation
/// refusal reasons are different from create/edit reasons).
/// </summary>
public enum TrainingSessionSignupResult
{
    /// <summary>Volunteer successfully added to the attendee list.</summary>
    SignedUp,

    /// <summary>Volunteer successfully removed from the attendee list.</summary>
    Cancelled,

    NotFound,

    /// <summary>The volunteer is already on the attendee list.</summary>
    AlreadySignedUp,

    /// <summary>Volunteer isn't on the attendee list (cancel was a no-op attempt).</summary>
    NotSignedUp,

    /// <summary>Sign-up refused — session has reached MaxAttendees capacity (decision Q1: ENFORCE).</summary>
    SessionFull,

    /// <summary>Sign-up refused — session was Cancelled before the volunteer arrived.</summary>
    SessionCancelled,

    /// <summary>
    /// Cancel-after-mark refused — volunteer was already marked attended;
    /// cancel must route through an admin (round-1 edge case, per
    /// <c>PLAN.md</c> Round-FR-2 spec).
    /// </summary>
    AlreadyMarkedAttended,
}

/// <summary>
/// Per-attendee mark input from the marker. The list passed to
/// <c>ITrainingSessionService.MarkAttendeesCompleteAsync</c> drives
/// both the <see cref="TrainingSessionAttendee.Attended"/> flag
/// (set for every entry) AND the <see cref="TrainingCompletion"/>
/// insert (only for entries with <see cref="Attended"/>=true when
/// the session has a <see cref="TrainingSession.TrainingContentId"/>).
/// </summary>
public sealed class AttendeeMark
{
    public string PersonUserId { get; set; } = null!;
    public bool Attended { get; set; }
}

/// <summary>
/// Round-FR-2: in-person scheduled training sessions with manual-completion
/// audit (see <c>PLAN.md</c> Round-FR-2). The service is the security
/// boundary for every write — pages and Razor handlers stay thin and the
/// service is what gets unit-tested.
/// </summary>
public interface ITrainingSessionService
{
    /// <summary>
    /// Coordinator/admin creates a session. Validates title/location,
    /// EndUtc &gt; StartUtc, MaxAttendees &gt; 0 (decision Q3 invariant);
    /// refuses if the optional TrainingContentId belongs to a different org.
    /// </summary>
    Task<TrainingSessionMutationResult> CreateAsync(
        int organizationId,
        string title,
        string? description,
        string location,
        DateTime startUtc,
        DateTime endUtc,
        int? maxAttendees,
        int? trainingContentId,
        string callerUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Coordinator/admin edits a not-yet-completed-not-cancelled session.
    /// Refuses <see cref="TrainingSessionMutationResult.AlreadyCancelled"/>
    /// and <see cref="TrainingSessionMutationResult.AlreadyCompleted"/>.
    /// Also refuses shrinking <c>maxAttendees</c> below the current
    /// attendee count with <see cref="TrainingSessionMutationResult.ValidationFailed"/>
    /// so a coord can't silently over-commit a session.
    /// </summary>
    Task<TrainingSessionMutationResult> EditAsync(
        int sessionId,
        string title,
        string? description,
        string location,
        DateTime startUtc,
        DateTime endUtc,
        int? maxAttendees,
        int? trainingContentId,
        string callerUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Coordinator/admin cancels a session (Status flips to Cancelled).
    /// Refuses <see cref="TrainingSessionMutationResult.AlreadyCompleted"/>
    /// — completed sessions are an audit-trail artifact, not a pending
    /// decision; see the marker's <c>MarkSingleCompleteAsync</c> for the
    /// non-session audit path.
    /// </summary>
    Task<TrainingSessionMutationResult> CancelAsync(
        int sessionId,
        string callerUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Scheduled sessions for an org in the next 60 days, ordering by
    /// StartUtc ascending. Eager-loads <see cref="TrainingSession.TrainingContent"/>
    /// + <see cref="TrainingSession.Attendees"/> so the page renders titles
    /// + roster in a single query.
    /// </summary>
    Task<List<TrainingSession>> ListUpcomingAsync(
        int organizationId,
        CancellationToken ct = default);

    /// <summary>
    /// Sessions for an org whose StartUtc &gt;= sinceUtc AND (completed
    /// OR cancelled OR end-time in the past). Eager-loads TrainingContent
    /// + Attendees. Used by the Sessions/Index "Past" tab.
    /// </summary>
    Task<List<TrainingSession>> ListPastAsync(
        int organizationId,
        DateTime sinceUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Single session with eager-loaded TrainingContent + Attendees.
    /// Null if not found. Used by Sessions/Detail.razor.
    /// </summary>
    Task<TrainingSession?> GetAsync(
        int sessionId,
        CancellationToken ct = default);

    /// <summary>
    /// Volunteer self-service. <paramref name="callerUserId"/> MUST
    /// match <paramref name="personUserId"/> — the service is the
    /// security boundary and refuses cross-user sign-up attempts
    /// (IDOR defense: a malicious page handler can't sign up a stranger
    /// under the volunteer's account). Refuses SessionFull /
    /// AlreadySignedUp / SessionCancelled. A volunteer who's not in the
    /// session's org returns <see cref="TrainingSessionSignupResult.NotFound"/>
    /// to avoid leaking session id existence to outsiders.
    /// </summary>
    Task<TrainingSessionSignupResult> SignUpAsync(
        int sessionId,
        string personUserId,
        string callerUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Volunteer self-service. <paramref name="callerUserId"/> MUST
    /// match <paramref name="personUserId"/> (IDOR defense — same as
    /// <see cref="SignUpAsync"/>). Refuses
    /// <see cref="TrainingSessionSignupResult.AlreadyMarkedAttended"/>
    /// post-marker per PLAN edge case (admin must mediate un-mark).
    /// </summary>
    Task<TrainingSessionSignupResult> CancelSignUpAsync(
        int sessionId,
        string personUserId,
        string callerUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Coordinator/admin marks attendance for a roster. For each
    /// <see cref="AttendeeMark"/>:
    /// (a) <see cref="TrainingSessionAttendee.Attended"/> is set
    /// unconditionally (true OR false, including no-shows);
    /// (b) when Attended=true AND the session has a <see cref="TrainingSession.TrainingContentId"/>,
    /// a <see cref="TrainingCompletion"/> row is written with
    /// CompletionSource=CoordinatorManual + MarkedCompleteByUserId + ManualCompletionNotes.
    ///
    /// Bypasses the engagement-eligibility gate (decision Q6): the marker
    /// asserts out-of-band competence. Notes REQUIRED on the bulk call
    /// (decision Q5) — empty markerNotes returns
    /// <see cref="TrainingSessionMutationResult.ValidationFailed"/>.
    ///
    /// Pre-loads all matching attendee rows in a single query before
    /// the per-row mutation loop (avoids N+1).
    /// </summary>
    Task<TrainingSessionMutationResult> MarkAttendeesCompleteAsync(
        int sessionId,
        string markerUserId,
        IReadOnlyList<AttendeeMark> attendeeResults,
        string markerNotes,
        CancellationToken ct = default);
}
