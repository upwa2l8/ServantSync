using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// A concrete scheduled time boundary for a <see cref="ServiceSlot"/>.
/// Created by coordinators ("we need a sound tech next Sunday at 9am") to
/// advertise an open shift that volunteers can self-sign-up for. Each
/// <see cref="Assignment"/> for the same <c>(ServiceSlotId, StartUtc)</c>
/// pair counts toward this occurrence's <see cref="EffectiveCapacity"/>;
/// once full, the volunteer-facing Sign up UI is disabled and the sign-up
/// action returns a validation failure.
///
/// We deliberately do NOT make <see cref="Assignment.PersonUserId"/>
/// nullable to represent an open shift — that would conflate a "help wanted
/// sign" with an "actual assignment" and breaks the moment a slot needs
/// capacity &gt; 1.
/// </summary>
public class SlotOccurrence
{
    public int Id { get; set; }

    public int ServiceSlotId { get; set; }
    public ServiceSlot ServiceSlot { get; set; } = null!;

    /// <summary>Inclusive start, stored in UTC. Matches <see cref="Assignment.StartUtc"/>.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Exclusive end, stored in UTC. Matches <see cref="Assignment.EndUtc"/>.</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>
    /// Optional per-occurrence capacity override. Null = fall back to
    /// <see cref="ServiceSlot.Capacity"/>. Set this on the rare occasion
    /// that a single occurrence needs a different headcount than the slot
    /// default (e.g. a holiday Sunday needs 4 greeters instead of 2).
    /// </summary>
    public int? CapacityOverride { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Returns the effective capacity for this occurrence (override or slot default).</summary>
    public int EffectiveCapacity => CapacityOverride ?? (ServiceSlot?.Capacity ?? 1);
}
