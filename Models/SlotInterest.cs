using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// Round-FR-7: junction table linking a <see cref="Person"/> to a
/// <see cref="ServiceSlot"/> they've explicitly subscribed to. Distinct
/// from <see cref="MinistryInterest"/>: that row is a ministry-level
/// preference (cascade-surfaces every slot in the ministry); this row is
/// fine-grained per-slot (cascade-surfaces only shifts for the specific
/// slot). Drives the FR-7 /Open "My slots" filter + the slot-detail
/// Subscribe toggle + the slot-coord Subscribers(N) roster panel.
/// <para>
/// Composite-unique on (<see cref="PersonUserId"/>, <see cref="ServiceSlotId"/>)
/// ensures a person cannot mark the same slot as "subscribed" twice.
/// </para>
/// <para>
/// <see cref="Source"/> is captured for audit but NOT surfaced as a UI badge
/// round 1 (per FR-7 spec decision Q-B2); slot coordinators care that
/// someone is reachable, not their origin journey. Round 2 could expose
/// Source via a "My Preferences" self-audit page if needed.
/// </para>
/// </summary>
public class SlotInterest
{
    public int Id { get; set; }

    public string PersonUserId { get; set; } = null!;
    public Person Person { get; set; } = null!;

    public int ServiceSlotId { get; set; }
    public ServiceSlot ServiceSlot { get; set; } = null!;

    public DateTime SubscribedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Captured at row-creation time so round-2 self-audit / data-quality
    /// tooling can distinguish a volunteer who clicked Subscribe proactively
    /// vs one whose row was auto-created as a follow-up to their /Open Sign-Up.
    /// See PLAN.md → Feature requests → Round-FR-7 for the full spec.
    /// </summary>
    public SlotInterestSource Source { get; set; } = SlotInterestSource.Explicit;

    [StringLength(500)]
    public string? Notes { get; set; }
}
