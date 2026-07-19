using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// A volunteer opportunity (a "ministry role" or service slot) within a
/// <see cref="Ministry"/>. Concrete scheduled instances are stored as
/// <see cref="Assignment"/> rows referencing this slot.
/// </summary>
public class ServiceSlot
{
    public int Id { get; set; }

    public int MinistryId { get; set; }
    public Ministry Ministry { get; set; } = null!;

    [Required, StringLength(160)]
    public string Name { get; set; } = null!;

    [StringLength(2000)]
    public string? Description { get; set; }

    [StringLength(200)]
    public string? Location { get; set; }

    /// <summary>Suggested length when scheduling an assignment. Optional.</summary>
    public int? DefaultDurationMinutes { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Maximum number of non-cancelled volunteers that may be assigned to a
    /// single occurrence of this slot. Default 1 to preserve historical
    /// single-volunteer UX; bump to N for "we need 3 referees" style
    /// opportunities. Per-occurrence overrides live on <see cref="SlotOccurrence"/>.
    /// </summary>
    public int Capacity { get; set; } = 1;

    /// <summary>
    /// Optional per-slot coordinator (a Person who is an
    /// OrganizationMember of the parent org). Distinct from the
    /// ministry-level coordinator so one ministry can have Sara
    /// running Greeters and a separate Dave running Welcome Desk
    /// without admins having to micro-manage the ministry layer.
    /// </summary>
    public string? CoordinatorPersonUserId { get; set; }
    public Person? CoordinatorPerson { get; set; }

    [StringLength(120), EmailAddress]
    public string? CoordinatorEmail { get; set; }

    [StringLength(40)]
    public string? CoordinatorPhone { get; set; }

    /// <summary>
    /// MudBlazor Material icon constant (e.g. "Icons.Material.Outlined.MusicNote").
    /// Set via the icon picker on the ServiceSlot Edit page. Null falls back to
    /// MinistryIcons.Default at render time.
    /// </summary>
    [StringLength(80)]
    public string? Icon { get; set; }

    /// <summary>Opportunity-specific training that volunteers must complete before scheduling.</summary>
    public ICollection<TrainingRequirement> TrainingRequirements { get; set; } = new List<TrainingRequirement>();

    public ICollection<Assignment> Assignments { get; set; } = new List<Assignment>();

    /// <summary>Concrete scheduled instances of this slot (each one an open-time that volunteers may sign up for).</summary>
    public ICollection<SlotOccurrence> Occurrences { get; set; } = new List<SlotOccurrence>();
}
