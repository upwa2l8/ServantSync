using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// A scheduled sports game between two <see cref="Team"/>s at a specific
/// <see cref="Arena"/>. The league ministry owns the game (for scoping /
/// queries) and is the natural unit for "show me every game in this
/// league". <c>HomeTeamId</c> and <c>ArenaId</c> are non-nullable; bye
/// weeks are represented by simply not creating a Game row for that week.
///
/// Conflict rule (enforced in <c>GameService</c>): two non-terminal games
/// at the same <c>ArenaId</c> cannot overlap in time. Standings math
/// (in <c>StandingsCalculator</c>) only counts <see cref="GameStatus.Played"/>
/// and <see cref="GameStatus.Forfeit"/> games.
/// </summary>
public class Game
{
    public int Id { get; set; }

    public int MinistryId { get; set; }
    public Ministry Ministry { get; set; } = null!;

    public int HomeTeamId { get; set; }
    public Team HomeTeam { get; set; } = null!;

    public int AwayTeamId { get; set; }
    public Team AwayTeam { get; set; } = null!;

    public int ArenaId { get; set; }
    public Arena Arena { get; set; } = null!;

    /// <summary>Inclusive start. Stored in UTC; UI converts to user's local time.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Exclusive end. Stored in UTC.</summary>
    public DateTime EndUtc { get; set; }

    public GameStatus Status { get; set; } = GameStatus.Scheduled;

    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>True when this game counts toward standings.</summary>
    public bool CountsForStandings(DateTime asOfUtc) =>
        Status == GameStatus.Played
        && HomeScore.HasValue
        && AwayScore.HasValue
        && StartUtc <= asOfUtc;
}
