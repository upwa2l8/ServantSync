using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// An athlete (typically a minor) on a <see cref="Team"/>. The player
/// record is intentionally separate from <see cref="Person"/>: a player
/// usually does not have a login. The <see cref="PrimaryContactPersonUserId"/>
/// links to a <see cref="Person"/> (typically a parent/guardian) who DOES
/// have a login and can view the team page.
///
/// Coaches and league admins see the full roster including contact info;
/// the primary contact sees only the player's own information plus
/// team-level data for teams where their child is rostered.
/// </summary>
public class Player
{
    public int Id { get; set; }

    public int TeamId { get; set; }
    public Team Team { get; set; } = null!;

    [Required, StringLength(80)]
    public string FirstName { get; set; } = null!;

    [Required, StringLength(80)]
    public string LastName { get; set; } = null!;

    public DateTime? DateOfBirth { get; set; }

    public int? JerseyNumber { get; set; }

    /// <summary>
    /// Sport-agnostic position label — "Goalkeeper" / "Forward" / "Pitcher"
    /// / "Point Guard" / "Setter" — whatever the league uses.
    /// </summary>
    [StringLength(80)]
    public string? Position { get; set; }

    /// <summary>
    /// The parent/guardian Person (the user with a login). Nullable so a
    /// league can roster a player whose primary contact isn't yet in the
    /// system. Phone/Email on this record are denormalized for the coach's
    /// convenience (so they can call a parent without a join).
    /// </summary>
    public string? PrimaryContactPersonUserId { get; set; }
    public Person? PrimaryContactPerson { get; set; }

    [StringLength(40)]
    public string? PrimaryContactPhone { get; set; }

    [StringLength(120), EmailAddress]
    public string? PrimaryContactEmail { get; set; }

    public DateTime JoinedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>When the player left the team (transfer, age-out, etc.).</summary>
    public DateTime? LeftUtc { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public string DisplayName => $"{FirstName} {LastName}".Trim();

    public bool IsActive(DateTime asOfUtc) => LeftUtc is null || LeftUtc > asOfUtc;
}
