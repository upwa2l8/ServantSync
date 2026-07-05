using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// A sports team within a ministry. When the ministry represents a sports
/// league (e.g. "Springfield Youth Soccer League"), its teams are the
/// competing units (e.g. "Eagles" U10 Boys, "Hawks" U10 Boys).
///
/// A team is identified within its ministry by <see cref="Name"/> (unique
/// per ministry). The <see cref="CoachPersonUserId"/> designates the
/// primary coach, who can edit the team's roster through
/// <c>TeamService</c>.
/// </summary>
public class Team
{
    public int Id { get; set; }

    public int MinistryId { get; set; }
    public Ministry Ministry { get; set; } = null!;

    [Required, StringLength(120)]
    public string Name { get; set; } = null!;

    public TeamAgeBracket AgeBracket { get; set; } = TeamAgeBracket.U10;

    /// <summary>Free-form gender label: "Boys" / "Girls" / "Coed".</summary>
    [StringLength(20)]
    public string? Gender { get; set; }

    [StringLength(2000)]
    public string? Description { get; set; }

    /// <summary>
    /// Optional primary coach. The coach is auto-authorized to manage
    /// this team's roster through <c>OrgAuthService.CanManageTeamAsync</c>.
    /// </summary>
    public string? CoachPersonUserId { get; set; }
    public Person? CoachPerson { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Player> Players { get; set; } = new List<Player>();
    public ICollection<Game> HomeGames { get; set; } = new List<Game>();
    public ICollection<Game> AwayGames { get; set; } = new List<Game>();
}
