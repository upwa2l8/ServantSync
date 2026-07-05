using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// A concrete scheduling event: a specific <see cref="Person"/> is assigned
/// to a specific <see cref="ServiceSlot"/> for a discrete UTC date/time
/// window. Conflict detection runs across these rows by Person, ignoring
/// non-active statuses.
/// </summary>
public class Assignment
{
    public int Id { get; set; }

    public string PersonUserId { get; set; } = null!;
    public Person Person { get; set; } = null!;

    public int ServiceSlotId { get; set; }
    public ServiceSlot ServiceSlot { get; set; } = null!;

    /// <summary>Inclusive start. Stored in UTC; UI converts to user's local time.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Exclusive end. Stored in UTC.</summary>
    public DateTime EndUtc { get; set; }

    public AssignmentStatus Status { get; set; } = AssignmentStatus.Scheduled;

    [StringLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
