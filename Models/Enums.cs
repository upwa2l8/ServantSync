namespace ServantSync.Models;

/// <summary>
/// A person's role within a given organization. A single person can hold
/// different roles in different organizations.
/// </summary>
public enum OrganizationRole
{
    Volunteer = 0,
    Coordinator = 1,
    Admin = 2,
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
