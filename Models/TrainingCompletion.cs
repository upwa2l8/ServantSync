using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// Records that a <see cref="Person"/> completed a specific version of a
/// <see cref="TrainingContent"/> on a given date. Drives validity checks at
/// scheduling time via <c>ExpiresUtc</c>.
/// </summary>
public class TrainingCompletion
{
    public int Id { get; set; }

    public string PersonUserId { get; set; } = null!;
    public Person Person { get; set; } = null!;

    public int TrainingContentId { get; set; }
    public TrainingContent TrainingContent { get; set; } = null!;

    /// <summary>Snapshot of the content version at the time the completion was recorded.</summary>
    public int TrainingContentVersion { get; set; }

    public DateTime CompletionUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Computed at recording time based on the requirement's cadence.</summary>
    public DateTime? ExpiresUtc { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    /// <summary>True when <see cref="ExpiresUtc"/> is null or in the future.</summary>
    public bool IsValid(DateTime asOfUtc) => ExpiresUtc is null || ExpiresUtc > asOfUtc;
}
