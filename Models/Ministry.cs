using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// A ministry inside an <see cref="Organization"/>. Owned by exactly one
/// organization; has zero or one coordinator (who is a Person).
/// </summary>
public class Ministry
{
    public int Id { get; set; }

    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    [Required, StringLength(160)]
    public string Name { get; set; } = null!;

    [StringLength(2000)]
    public string? Description { get; set; }

    /// <summary>Optional Person (UserId) who coordinates this ministry.</summary>
    public string? CoordinatorPersonUserId { get; set; }
    public Person? CoordinatorPerson { get; set; }

    [StringLength(120), EmailAddress]
    public string? CoordinatorEmail { get; set; }

    [StringLength(40)]
    public string? CoordinatorPhone { get; set; }

    public ICollection<ServiceSlot> ServiceSlots { get; set; } = new List<ServiceSlot>();

    /// <summary>
    /// Optional parent ministry. When set, this ministry is a sub-ministry
    /// of the parent (e.g. a "Springfield Youth Soccer League" ministry
    /// might have "Referees" / "Concessions" / "Devotion" as sub-ministries,
    /// each with its own coordinator). The parent coordinator manages the
    /// sub-ministries through normal ministry CRUD; the sub-ministry
    /// coordinator manages only its own slots.
    /// </summary>
    public int? ParentMinistryId { get; set; }
    public Ministry? ParentMinistry { get; set; }
    public ICollection<Ministry> SubMinistries { get; set; } = new List<Ministry>();

    /// <summary>Sports teams that belong to this ministry (e.g. a "league" ministry owns its teams).</summary>
    public ICollection<Team> Teams { get; set; } = new List<Team>();
}
