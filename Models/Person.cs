using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace ServantSync.Models;

/// <summary>
/// Domain record for a human (volunteer, coordinator, admin).
/// Extends ASP.NET Core Identity by holding a 1:1 link to <see cref="IdentityUser"/>
/// via <see cref="UserId"/> — both the primary key and the foreign key.
/// </summary>
public class Person
{
    /// <summary>PK and FK. Matches <see cref="IdentityUser.Id"/>.</summary>
    public string UserId { get; set; } = null!;

    public IdentityUser User { get; set; } = null!;

    /// <summary>Round-FR-3: true for manually-added records with a
    /// placeholder <see cref="IdentityUser"/> (no usable login). The
    /// placeholder IdentityUser has <c>LockoutEnabled=true</c> +
    /// <c>LockoutEnd=9999-12-31</c> so the auth pipeline refuses login.
    /// The flag is what distinguishes real accounts from pending-claim
    /// accounts in the admin stub-management UI; it is NOT used to
    /// gate login (the lockout does that). Default <c>false</c> for
    /// every existing row — the migration adds the column with
    /// <c>NOT NULL DEFAULT 0</c> so all pre-FR-3 People remain real.</summary>
    public bool IsStub { get; set; }

    [Required, StringLength(80)]
    public string FirstName { get; set; } = null!;

    [Required, StringLength(80)]
    public string LastName { get; set; } = null!;

    [StringLength(40)]
    public string? Phone { get; set; }

    /// <summary>Round-FR-3: optional email captured at stub creation.
    /// For real (non-stub) People, this denormalizes
    /// <see cref="IdentityUser.Email"/> — the IdentityUser remains the
    /// source of truth and a future round could backfill. For stubs,
    /// this is the only email field surfaced to the volunteer (the
    /// placeholder IdentityUser's email is not user-facing) and
    /// powers the email-match secondary claim flow at
    /// <c>/Account/Register</c>. Indexed non-unique because two stubs
    /// can legitimately share an email (an explicit edge case in the
    /// FR-3 spec — Register warns and refuses to auto-link on
    /// collision, admin resolves manually). 254-char ceiling matches
    /// the RFC 5321 SMTP-path maximum.</summary>
    [StringLength(254), EmailAddress]
    public string? Email { get; set; }

    public DateTime? DateOfBirth { get; set; }

    /// <summary>Date the most recent background check was completed.</summary>
    public DateTime? BackgroundCheckDate { get; set; }

    [StringLength(1000)]
    public string? SkillsNotes { get; set; }

    public ICollection<OrganizationMembership> Memberships { get; set; } = new List<OrganizationMembership>();

    public ICollection<TrainingCompletion> TrainingCompletions { get; set; } = new List<TrainingCompletion>();

    public string DisplayName => $"{FirstName} {LastName}".Trim();
}
