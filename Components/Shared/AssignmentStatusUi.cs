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
}
