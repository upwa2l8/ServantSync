using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// A requirement that an assigned volunteer must satisfy a particular
/// <see cref="TrainingContent"/>, attached to either an
/// <see cref="Organization"/> (org-wide, every volunteer) or a
/// <see cref="ServiceSlot"/> (opportunity-specific).
///
/// Polarity is enforced by exactly-one of the two nullable FKs being set,
/// guarded by a check constraint in the schema.
/// </summary>
public class TrainingRequirement
{
    public int Id { get; set; }

    public int? OrganizationId { get; set; }
    public Organization? Organization { get; set; }

    public int? ServiceSlotId { get; set; }
    public ServiceSlot? ServiceSlot { get; set; }

    public int TrainingContentId { get; set; }
    public TrainingContent TrainingContent { get; set; } = null!;

    public TrainingCadence Cadence { get; set; } = TrainingCadence.Yearly;

    /// <summary>Only used when <see cref="Cadence"/> is <see cref="TrainingCadence.EveryMonths"/>.</summary>
    public int? CadenceMonths { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    /// <summary>Convenience: which scope this requirement lives in.</summary>
    public string Scope => OrganizationId.HasValue ? "Organization" : "ServiceSlot";
}
