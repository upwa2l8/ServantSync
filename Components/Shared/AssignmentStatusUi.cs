using MudBlazor;
using ServantSync.Models;

namespace ServantSync.Components.Shared;

/// <summary>
/// UI helpers for the <see cref="AssignmentStatus"/> enum. Centralized so the
/// Bootstrap bg-* class mapping is consistent across pages and is testable
/// from a unit-test project.
/// </summary>
public static class AssignmentStatusUi
{
    /// <summary>
    /// Returns a Bootstrap <c>bg-*</c> class suffix for a status.
    /// Always returns a non-null, non-empty string.
    /// </summary>
    public static string ColorFor(AssignmentStatus s) => s switch
    {
        AssignmentStatus.Scheduled => "primary",
        AssignmentStatus.Tentative => "warning",
        AssignmentStatus.Completed => "success",
        AssignmentStatus.Cancelled => "secondary",
        AssignmentStatus.NoShow    => "danger",
        _                          => "secondary",
    };

    /// <summary>
    /// Returns a Bootstrap <c>bg-*</c> class suffix for a <see cref="GameStatus"/>.
    /// Same vocabulary as <see cref="ColorFor(AssignmentStatus)"/> so the
    /// calendar overlay reads consistently.
    /// </summary>
    public static string ColorFor(GameStatus s) => s switch
    {
        GameStatus.Scheduled  => "primary",
        GameStatus.InProgress => "warning",
        GameStatus.Played     => "success",
        GameStatus.Cancelled  => "secondary",
        GameStatus.Postponed  => "info",
        GameStatus.Forfeit    => "danger",
        _                     => "secondary",
    };

    /// <summary>
    /// Returns a <see cref="MudBlazor.Color"/> for an <see cref="AssignmentStatus"/>.
    /// Phase-2 follow-up: the un-migrated pages still call the string
    /// overload above; the migrated pages (Home, Open, MySchedule,
    /// Dashboard, ServiceSlots/Detail, …) use this overload to drive
    /// <c>&lt;MudChip Color="..."&gt;</c>. Vocabulary is kept in sync with
    /// <see cref="ColorFor(AssignmentStatus)"/> so a status's color is
    /// stable across both Bootstrap and MudBlazor surfaces.
    /// </summary>
    public static Color MudColorFor(AssignmentStatus s) => s switch
    {
        AssignmentStatus.Scheduled => Color.Primary,
        AssignmentStatus.Tentative => Color.Warning,
        AssignmentStatus.Completed => Color.Success,
        AssignmentStatus.Cancelled => Color.Default,
        AssignmentStatus.NoShow    => Color.Error,
        _                          => Color.Default,
    };

    /// <summary>
    /// <see cref="MudBlazor.Color"/> overload for <see cref="GameStatus"/>.
    /// </summary>
    public static Color MudColorFor(GameStatus s) => s switch
    {
        GameStatus.Scheduled  => Color.Primary,
        GameStatus.InProgress => Color.Warning,
        GameStatus.Played     => Color.Success,
        GameStatus.Cancelled  => Color.Default,
        GameStatus.Postponed  => Color.Info,
        GameStatus.Forfeit    => Color.Error,
        _                     => Color.Default,
    };
}
