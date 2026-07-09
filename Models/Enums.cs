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
