using ServantSync.Models;

namespace ServantSync.Services;

/// <summary>
/// Coordinates the scheduling rules: per-person global conflict detection
/// plus training-validity enforcement. Designed for use inside Blazor
/// Server handlers where we want synchronous validation feedback.
/// </summary>
public interface IAssignmentService
{
    /// <summary>
    /// Checks the proposed assignment against existing non-cancelled
    /// assignments for the same person, and against training requirements
    /// for the slot's ministry's organization and the slot itself.
    /// </summary>
    Task<AssignmentValidationResult> ValidateAsync(
        string personUserId,
        int serviceSlotId,
        DateTime startUtc,
        DateTime endUtc,
        int? excludeAssignmentId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Persists a new assignment after running <see cref="ValidateAsync"/>.
    /// Returns a failure result without persisting on validation error.
    /// </summary>
    Task<AssignmentValidationResult> AssignAsync(
        string personUserId,
        int serviceSlotId,
        DateTime startUtc,
        DateTime endUtc,
        string? notes = null,
        CancellationToken ct = default);

    /// <summary>Lists existing assignments for a person within a window, with slot+ministry eager-loaded.</summary>
    Task<List<Assignment>> ListForPersonAsync(
        string personUserId,
        DateTime fromUtc,
        DateTime toUtc,
        CancellationToken ct = default);

    /// <summary>
    /// Expands a weekly-recurrence into concrete <see cref="Assignment"/> rows.
    /// For each occurrence (dayOfWeek ≥ startDate, every 7 days until endDate),
    /// runs <see cref="ValidateAsync"/>; occurrences that conflict or have
    /// missing training are reported in <see cref="ScheduleSeriesResult.Skipped"/>
    /// with reasons. Cap: 500 occurrences per call.
    /// </summary>
    Task<ScheduleSeriesResult> ScheduleSeriesAsync(
        string personUserId,
        int serviceSlotId,
        DayOfWeek dayOfWeek,
        TimeSpan localStartTime,
        int durationMinutes,
        DateTime startDate,
        DateTime endDate,
        string timeZoneId,
        CancellationToken ct = default);

    /// <summary>
    /// Coordinator-only: create an open <see cref="SlotOccurrence"/> for a
    /// slot without pre-assigning anyone. The shift then shows up on the
    /// volunteer-facing `/Open` browse page; volunteers self-sign-up call
    /// <see cref="AssignAsync"/>. Capacity falls back to <c>slot.Capacity</c>
    /// when <paramref name="capacityOverride"/> is null.
    /// </summary>
    Task<SlotOccurrenceCreationResult> CreateSlotOccurrenceAsync(
        int serviceSlotId,
        DateTime startUtc,
        DateTime endUtc,
        string? notes,
        int? capacityOverride,
        CancellationToken ct = default);

    /// <summary>
    /// Coordinator-only: weekly recurrence of open <see cref="SlotOccurrence"/>
    /// rows. For each occurrence (dayOfWeek ≥ startDate, every 7 days until
    /// endDate), attempts to create a <see cref="SlotOccurrence"/> with the
    /// given <paramref name="capacityOverride"/>. Occurrences that conflict
    /// with an existing open shift at the same <c>(slot, StartUtc)</c> pair,
    /// or whose slot is inactive, are reported in <see cref="OpenShiftSeriesResult.Skipped"/>
    /// with reasons. Cap: 500 occurrences per call — mirrors
    /// <see cref="ScheduleSeriesAsync"/>.
    /// </summary>
    Task<OpenShiftSeriesResult> ScheduleOpenShiftSeriesAsync(
        int serviceSlotId,
        DayOfWeek dayOfWeek,
        TimeSpan localStartTime,
        int durationMinutes,
        DateTime startDate,
        DateTime endDate,
        string timeZoneId,
        int? capacityOverride,
        string? notes,
        CancellationToken ct = default);

    /// <summary>
    /// Volunteer-facing: list upcoming open <see cref="SlotOccurrence"/>
    /// rows across all organizations the volunteer is a member of, joined
    /// with current sign-up counts and a flag for whether THIS volunteer
    /// is already signed up. Shifts the volunteer can't take due to
    /// missing training are still returned but flagged so the UI can
    /// disable the Sign-up button.
    /// </summary>
    /// <param name="ministryIdsFilter">
    /// Optional whitelist: when supplied and non-null, only occurrences
    /// whose slot's <c>MinistryId</c> is in this collection are returned.
    /// Used by <c>Components/Pages/Open.razor</c> to narrow the default
    /// list down to the user's <c>MinistryInterest</c> rows. Null or empty
    /// returns every open occurrence across the volunteer's orgs (the
    /// "show all my orgs" fallback when the user hasn't joined any).
    /// </param>
    Task<List<OpenSlotOccurrenceView>> ListOpenSlotOccurrencesAsync(
        string personUserId,
        DateTime fromUtc,
        DateTime toUtc,
        IReadOnlyCollection<int>? ministryIdsFilter = null,
        CancellationToken ct = default);
}

/// <summary>Outcome of a coordinator open-shift creation.</summary>
public record SlotOccurrenceCreationResult(
    bool Succeeded,
    SlotOccurrence? Occurrence,
    string? Error)
{
    public static SlotOccurrenceCreationResult Ok(SlotOccurrence o) => new(true, o, null);
    public static SlotOccurrenceCreationResult Fail(string error) => new(false, null, error);
}

/// <summary>
/// One row in the volunteer-facing `/Open` list. Combines a
/// <see cref="SlotOccurrence"/> with current sign-up count and the
/// current volunteer's training-compliance + sign-up status.
/// </summary>
public record OpenSlotOccurrenceView(
    int OccurrenceId,
    int ServiceSlotId,
    string SlotName,
    int MinistryId,
    string MinistryName,
    int OrganizationId,
    DateTime StartUtc,
    DateTime EndUtc,
    string? Location,
    int Capacity,
    int SignedUpCount,
    bool AlreadySignedUp,
    bool TrainingCompliant,
    IReadOnlyList<string> MissingTrainings);

/// <summary>
/// Outcome of a recurring open-shift series expansion. Parallel to
/// <see cref="ScheduleSeriesResult"/> but for the open-shift path.
/// </summary>
public record OpenShiftSeriesResult(
    IReadOnlyList<SlotOccurrence> Created,
    IReadOnlyList<ScheduleSeriesSkipped> Skipped,
    DateTime SeriesFirstUtc,
    DateTime SeriesLastUtc,
    bool CapReached);

/// <summary>Outcome of an assignment validation. Use <see cref="Succeeded"/> to short-circuit happy path checks.</summary>
public record AssignmentValidationResult(
    bool Succeeded,
    Assignment? Assignment,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<string> MissingTrainings)
{
    public static AssignmentValidationResult Ok(Assignment a) =>
        new(true, a, Array.Empty<string>(), Array.Empty<string>());

    public static AssignmentValidationResult Fail(
        IReadOnlyList<string> conflicts,
        IReadOnlyList<string> missingTrainings) =>
        new(false, null, conflicts, missingTrainings);
}

/// <summary>Outcome of a recurring-series expansion. Includes both successes and skip-with-reasons.</summary>
public record ScheduleSeriesResult(
    IReadOnlyList<Assignment> Created,
    IReadOnlyList<ScheduleSeriesSkipped> Skipped,
    DateTime SeriesFirstUtc,
    DateTime SeriesLastUtc,
    bool CapReached);

/// <summary>One occurrence that couldn't be scheduled and why.</summary>
public record ScheduleSeriesSkipped(
    DateTime ProposedStartUtc,
    DateTime ProposedEndUtc,
    IReadOnlyList<string> Reasons);
