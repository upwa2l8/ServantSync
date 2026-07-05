using ServantSync.Models;

namespace ServantSync.Services;

public interface ISlotDocumentService
{
    /// <summary>
    /// Persists the uploaded file to disk and inserts a <see cref="SlotDocument"/>
    /// row. The caller (Blazor page) is responsible for reading the file from
    /// the request and passing the stream + metadata. Coordinator-only —
    /// throws if the user isn't an Admin/Coordinator of the slot's parent org.
    /// </summary>
    Task<SlotDocument> UploadAsync(
        int slotId,
        Stream content,
        string originalFileName,
        string? contentType,
        long sizeBytes,
        string title,
        string? description,
        string? category,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces the file of an existing document while keeping the same
    /// row ID (so the download URL stays stable). The new file is written
    /// to disk, the old file is deleted, and the row's file metadata +
    /// uploader + timestamp are updated to reflect the new upload. The
    /// title, description, and category are NOT changed — the goal is
    /// "swap the bytes", not "upload a different doc". Permission check
    /// is the same as <see cref="DeleteAsync"/>: org coordinator/admin,
    /// ministry coordinator (transitive), or the original uploader if
    /// still a member of the org.
    /// </summary>
    Task<SlotDocument> ReplaceAsync(
        int existingDocId,
        Stream content,
        string originalFileName,
        string? contentType,
        long sizeBytes,
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Returns the documents for a slot grouped by category (uncategorized
    /// comes last), each inner list ordered by upload date descending.
    /// Any org member can read.
    /// </summary>
    Task<List<SlotDocumentGroup>> ListGroupedByCategoryAsync(int slotId, CancellationToken ct = default);

    /// <summary>
    /// Returns a single document by id (for the download endpoint). Returns
    /// null if not found.
    /// </summary>
    Task<SlotDocument?> GetAsync(int documentId, CancellationToken ct = default);

    /// <summary>
    /// Deletes the file from disk and the row from the DB. Allowed if the
    /// caller is an org coordinator/admin OR the original uploader.
    /// Throws if not authorized or the document doesn't exist.
    /// </summary>
    Task DeleteAsync(int documentId, string userId, CancellationToken ct = default);

    /// <summary>
    /// True if the user can upload/delete documents for the given slot
    /// (org Admin/Coordinator for the slot's parent organization, or the
    /// slot's ministry coordinator transitively).
    /// </summary>
    Task<bool> CanManageSlotAsync(int slotId, string userId, CancellationToken ct = default);
}

/// <summary>A grouping of <see cref="SlotDocument"/>s by their free-form category label.</summary>
public record SlotDocumentGroup(string? Category, IReadOnlyList<SlotDocument> Documents);
