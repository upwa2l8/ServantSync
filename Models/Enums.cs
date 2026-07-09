namespace ServantSync.Models;

/// <summary>
/// A person's role within a given organization. A single person can hold
/// different roles in different organizations.
/// </summary>
public enum OrganizationRole
{
    Volunteer = 0,
    /// <summary>Round-FR-5: previously named Coordinator; the underlying
    /// int value (1) is preserved so existing database rows are
    /// unaffected. UI/visibility controls and "this person manages this
    /// ministry" delegations now live here; actual scoping of data is
    /// driven by <c>Ministry.CoordinatorPersonUserId</c> /
    /// <c>ServiceSlot.CoordinatorPersonUserId</c>, not by this enum.</summary>
    MinistryDirector = 1,
    Admin = 2,
    /// <summary>Round-FR-5: NEW role. Manages a specific subset of
    /// <c>ServiceSlot</c>s (the ones whose
    /// <c>CoordinatorPersonUserId</c> matches their UserId). Sits under
    /// MinistryDirector in the visibility hierarchy; cannot manage the
    /// whole org. Purely additive (int value 3).</summary>
    SlotCoordinator = 3,
}

/// <summary>
/// Media format for a training resource.
/// </summary>
public enum TrainingFormat
{
    Video = 0,
    Slideshow = 1,
    Pdf = 2,
}

/// <summary>
/// How often a training must be re-completed to remain valid.
/// </summary>
public enum TrainingCadence
{
    OneTime = 0,
    Yearly = 1,
    EveryMonths = 2,
}

/// <summary>
/// Lifecycle state of a scheduled volunteer assignment.
/// </summary>
public enum AssignmentStatus
{
    Scheduled = 0,
    Tentative = 1,
    Completed = 2,
    Cancelled = 3,
    NoShow = 4,
}

/// <summary>
/// Age bracket a sports team competes in. Sport-agnostic and intentionally
/// coarse (U6 … U18, plus Adult) so a single league can be modeled
/// regardless of sport.
/// </summary>
public enum TeamAgeBracket
{
    U6 = 0,
    U8 = 1,
    U10 = 2,
    U12 = 3,
    U14 = 4,
    U16 = 5,
    U18 = 6,
    Adult = 7,
}

/// <summary>
/// Lifecycle state of a sports game. Standings only count <see cref="Played"/>
/// games; <see cref="Cancelled"/>, <see cref="Postponed"/>, and
/// <see cref="Forfeit"/> are tracked but excluded from W/L/D math.
/// </summary>
public enum GameStatus
{
    Scheduled = 0,
    InProgress = 1,
    Played = 2,
    Cancelled = 3,
    Postponed = 4,
    Forfeit = 5,
}

/// <summary>
/// Action recorded on a <see cref="SystemAdminGrantAudit"/> row. The
/// stored int matches the row order — Grant = 0, Revoke = 1 — but
/// the codebase reads through the enum so a future re-order is safe.
/// </summary>
public enum SystemAdminAuditAction
{
    Grant = 0,
    Revoke = 1,
}

/// <summary>
/// Round-FR-2: how a <see cref="TrainingCompletion"/> was recorded.
/// <see cref="UserOnline"/> is the round-AV-and-prior
/// engagement-verified mark path (the volunteer's browser proved
/// they engaged with the content). <see cref="CoordinatorManual"/>
/// is the new in-person-session batch-mark path (a coordinator /
/// admin observed attendance at a <see cref="TrainingSession"/>).
/// <see cref="CoordinatorManualSingle"/> is the new ad-hoc
/// single-volunteer mark path (a coordinator / admin asserts
/// competence out-of-band, no session required).
/// </summary>
public enum TrainingCompletionSource
{
    UserOnline = 0,
    CoordinatorManual = 1,
    CoordinatorManualSingle = 2,
}

/// <summary>
/// Round-FR-2: lifecycle state of a <see cref="TrainingSession"/>.
/// <see cref="Scheduled"/> on creation; <see cref="Cancelled"/>
/// if cancelled pre-event; <see cref="Completed"/> after the
/// marker finalizes attendance via
/// <c>ITrainingSessionService.MarkAttendeesCompleteAsync</c>.
/// </summary>
public enum TrainingSessionStatus
{
    Scheduled = 0,
    Completed = 1,
    Cancelled = 2,
}

/// <summary>
/// Round-FR-6: per-org "training due soon" grid — the status drives the
/// Bootstrap badge color on the DueSoon.razor page (red = Overdue,
/// yellow = DueSoon, green = Compliant, gray = NotRequired). The matrix
/// is computed off the existing <see cref="TrainingCompletion.ExpiresUtc"/>
/// field (NO data-model change required for this round). See PLAN.md →
/// Feature requests → Round-FR-6 for the full decision ledger; spec
/// covers 10 resolved decisions including stub inclusion, OneTime
/// never-tracked carve-out, and stub-vs-foreign-org RBAC semantics.
/// </summary>
public enum TrainingDueSoonStatus
{
    /// <summary>
    /// Most-recent completion's <c>ExpiresUtc</c> is in the past, OR no
    /// completion record exists for a Yearly / EveryMonths requirement.
    /// The "no record = overdue" carve-out matches the existing
    /// AssignmentService training-gate semantics at FR-2.
    /// </summary>
    Overdue = 0,

    /// <summary>
    /// Most-recent completion's <c>ExpiresUtc</c> is within the next 30
    /// days AND in the future. Subset of "at-risk" surfaced under the
    /// Due-in-30-days filter chip.
    /// </summary>
    DueSoon = 1,

    /// <summary>
    /// Most-recent completion's <c>ExpiresUtc</c> is null (OneTime
    /// cadence: forever valid once completed) OR &gt; 30 days in the
    /// future. Filtered out by default; surfaced only under the
    /// CompletedRecently filter chip (where a recent completion still
    /// qualifies for the audit-validating view).
    /// </summary>
    Compliant = 2,

    /// <summary>
    /// OneTime cadence requirement with NO completion record. Per spec
    /// decision Q5 — kept in the enum even though the grid excludes
    /// these from All-at-risk by default; a future-round coordinator
    /// could flip the policy by widening the surface here.
    /// </summary>
    NotRequired = 3,
}

/// <summary>
/// Round-FR-6: filter chip selection on the DueSoon.razor page. Defaults
/// to <see cref="AllAtRisk"/> so the user's verbatim ask — "show me who
/// needs training" — is satisfied on first paint without a click.
/// </summary>
public enum TrainingDueSoonFilter
{
    AllAtRisk = 0,
    OverdueOnly = 1,
    DueIn30Days = 2,
    CompletedRecently = 3,
}

/// <summary>
/// Round-FR-6: sort toggle selection. <see cref="ByUrgency"/> is the
/// default per spec decision Q6 (overdue first by days-overdue DESC,
/// then due-soon by days-until ASC) so a coordinator scanning the grid
/// sees the most urgent rows at the top.
/// </summary>
public enum TrainingDueSoonSort
{
    ByUrgency = 0,
    ByPersonName = 1,
    ByContentTitle = 2,
}

/// <summary>
/// Round-FR-7: how a <see cref="SlotInterest"/> row was created.
/// <see cref="Explicit"/> is the volunteer-clicked-Subscribe-button path
/// (the canonical self-service toggle on <c>ServiceSlots/Detail.razor</c>,
/// the per-slot-row pill on <c>Ministries/Detail.razor</c>, and the
/// per-shift-card Subscribe on <c>Open.razor</c>).
/// <see cref="AutoFromAssignment"/> is the auto-create-follow-up path
/// fired AFTER a successful <c>AssignmentService.SignUpAsync</c> on the
/// <c>Open.razor</c> page — per FR-7 spec Q1 decision (user picked YES).
/// NOT surfaced as a UI badge on the slot-coord roster round 1
/// (Q-B2 decision); captured for round-2 self-audit / data-quality
/// tooling. Coordinator-driven <c>AssignmentService.AssignAsync</c>
/// calls (the management side from <c>ServiceSlots/Schedule.razor</c>)
/// DO NOT trigger auto-subscribe — only self-sign-ups through /Open do,
/// so coordinators can't pollute volunteer preferences.
/// </summary>
public enum SlotInterestSource
{
    /// <summary>Volunteer clicked the Subscribe button.</summary>
    Explicit = 0,

    /// <summary>Auto-created follow-up to a successful <c>Open.razor</c> Sign-Up.</summary>
    AutoFromAssignment = 1,
}
