using System.ComponentModel.DataAnnotations;

namespace ServantSync.Models;

/// <summary>
/// A file (PDF, DOCX, image, etc.) that a coordinator has shared with the
/// volunteers who hold a particular <see cref="ServiceSlot"/>. Stored on
/// disk under <c>wwwroot/uploads/slots/{ServiceSlotId}/</c>; the database
/// row holds the metadata + display fields. <see cref="Category"/> is a
/// free-form grouping label so coordinators can organize documents
/// ("Music", "Setup", "Announcements", etc.) without a schema change.
/// </summary>
public class SlotDocument
{
    public int Id { get; set; }

    public int ServiceSlotId { get; set; }
    public ServiceSlot ServiceSlot { get; set; } = null!;

    [Required, StringLength(160)]
    public string Title { get; set; } = null!;

    [StringLength(2000)]
    public string? Description { get; set; }

    /// <summary>Free-form grouping label. Null/empty = uncategorized.</summary>
    [StringLength(80)]
    public string? Category { get; set; }

    /// <summary>Relative path under wwwroot (e.g. "uploads/slots/3/abc.pdf").</summary>
    [Required, StringLength(500)]
    public string FilePath { get; set; } = null!;

    [Required, StringLength(255)]
    public string OriginalFileName { get; set; } = null!;

    [StringLength(100)]
    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    [Required]
    public string UploadedByUserId { get; set; } = null!;

    /// <summary>
    /// The uploader as a domain <see cref="Person"/>. The FK chain is
    /// <c>SlotDocuments.UploadedByUserId</c> → <c>People.UserId</c> (PK of
    /// Person, also FK to <c>AspNetUsers.Id</c>). EF Core supports this
    /// "FK to a shared-key table" pattern. The navigation is to
    /// <see cref="Person"/> rather than <c>IdentityUser</c> so the UI can
    /// show <see cref="Person.DisplayName"/> instead of the email.
    /// </summary>
    public Person? UploadedByPerson { get; set; }

    public DateTime UploadedUtc { get; set; } = DateTime.UtcNow;
}
