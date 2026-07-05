using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// A playing arena (field, court, diamond, gym) shared by any league in the
/// organization. Scoped to <see cref="Organization"/> rather than to a single
/// <see cref="Ministry"/> so multiple leagues in the same org can share
/// arenas. The arena is the single source of truth for "where" a game
/// happens — the game's <c>ArenaId</c> is non-nullable.
///
/// <see cref="GameService"/> enforces that no two active games overlap in
/// time at the same arena.
/// </summary>
public class Arena
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [Required, StringLength(120)]
    public string Name { get; set; } = null!;

    [StringLength(200)]
    public string? Address { get; set; }

    /// <summary>
    /// Free-form surface type — "Grass" / "Turf" / "Hardwood" / "Clay" /
    /// "Diamond" — so the league can use its own vocabulary without a new
    /// schema change.
    /// </summary>
    [StringLength(40)]
    public string? SurfaceType { get; set; }

    public int? Capacity { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public ICollection<Game> Games { get; set; } = new List<Game>();
}
