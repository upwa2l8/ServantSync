using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// Volunteer sign-up for a <see cref="TrainingSession"/>. Round-FR-2
/// implementation.
///
/// Composite-unique on (TrainingSessionId, PersonUserId) enforces
/// one-signup-per-volunteer (handled in DbContext config). <see cref="Attended"/>
/// is null until the marker records it (true = attended, false = no-show).
/// The marker's identity and notes live on the <see cref="TrainingCompletion"/>
/// rows the marker writes via
/// <c>ITrainingSessionService.MarkAttendeesCompleteAsync</c> — this row
/// only carries the attendance outcome, not the marker identity.
/// </summary>
public class TrainingSessionAttendee
{
    public int Id { get; set; }

    public int TrainingSessionId { get; set; }
    public TrainingSession TrainingSession { get; set; } = null!;

    public string PersonUserId { get; set; } = null!;
    public Person Person { get; set; } = null!;

    public DateTime SignedUpUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Null until the marker records it. true = attended,
    /// false = no-show. Editing post-marker only via a follow-up
    /// admin-only flow (not in round 1).
    /// </summary>
    public bool? Attended { get; set; }
}
