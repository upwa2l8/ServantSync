using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// Round-FR-3: a one-time-use claim token that links a manually-added
/// stub <see cref="Person"/> to a real <c>IdentityUser</c> when the
/// volunteer eventually self-registers at <c>/Account/Register?claim=…</c>.
/// The raw token is returned to the admin ONCE at creation (printed-paper
/// handoff to the volunteer) and never persisted; only the SHA-256 hash
/// lives on disk. The token expires after 30 days; admin can rotate to
/// extend. Once a token is consumed (or rotated, or expired), it is
/// terminal — the <see cref="ClaimedUtc"/> field repurposes to mean
/// "terminal state" across all three (consumed / rotated / expired) in
/// round 1; a future round could split into a separate <c>IsRevoked</c>
/// column for clarity.
///
/// Cascade-on-Person-delete matches every other Person FK in the
/// codebase (OrganizationMembership, Assignment, TrainingCompletion,
/// TrainingActivity) — tokens for a deleted Person are useless.
/// <see cref="CreatedByUserId"/> is a plain string with no FK nav,
/// matching the audit-trail-preserves-actor pattern in
/// <c>SystemAdminGrantAudit</c> and <c>TrainingSession.CreatedByUserId</c>:
/// deleting the admin who created a token must NOT vaporize the
/// forensic record.
/// </summary>
public class PersonClaimToken
{
    public int Id { get; set; }

    /// <summary>FK to the stub <see cref="Person.UserId"/> this token claims.</summary>
    public string PersonUserId { get; set; } = null!;

    public Person Person { get; set; } = null!;

    /// <summary>SHA-256 hex of the 32-byte random raw token (64 hex chars).
    /// The raw token is never stored — only returned to the admin ONCE
    /// at creation/rotation, then discarded. Indexed unique so two
    /// tokens can't hash-collide (SHA-256 collision-resistance
    /// notwithstanding, the index enforces the application's
    /// one-active-token-per-stub invariant at the DB layer too).</summary>
    [Required, StringLength(64)]
    public string TokenHash { get; set; } = null!;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    /// <summary>30-day default. Admin can rotate to extend.</summary>
    public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow.AddDays(30);

    /// <summary>Set on consume OR rotate. Round 1 repurposes this to
    /// mean "terminal state" across all three (consumed / rotated /
    /// expired). A null value means the token is still active and
    /// claimable. A future round could split into a separate
    /// <c>IsRevoked</c> column if the distinction matters in queries.</summary>
    public DateTime? ClaimedUtc { get; set; }

    /// <summary>The admin who created this token. Plain string, no FK
    /// nav, matching the audit-trail-preserves-actor pattern. Storing
    /// the actor's userId even after their IdentityUser is deleted
    /// lets forensics answer "who issued the claim token for stub
    /// X?" after the admin's account is gone.</summary>
    public string CreatedByUserId { get; set; } = null!;
}
