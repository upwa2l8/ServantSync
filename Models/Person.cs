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

    [Required, StringLength(80)]
    public string FirstName { get; set; } = null!;

    [Required, StringLength(80)]
    public string LastName { get; set; } = null!;

    [StringLength(40)]
    public string? Phone { get; set; }

    public DateTime? DateOfBirth { get; set; }

    /// <summary>Date the most recent background check was completed.</summary>
    public DateTime? BackgroundCheckDate { get; set; }

    [StringLength(1000)]
    public string? SkillsNotes { get; set; }

    public ICollection<OrganizationMembership> Memberships { get; set; } = new List<OrganizationMembership>();

    public ICollection<TrainingCompletion> TrainingCompletions { get; set; } = new List<TrainingCompletion>();

    public string DisplayName => $"{FirstName} {LastName}".Trim();
}
