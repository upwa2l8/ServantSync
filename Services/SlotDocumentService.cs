using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Services;

public class SlotDocumentService : ISlotDocumentService
{
    /// <summary>Hard cap on a single upload, in bytes. Delegates to <see cref="SlotUploadLimits"/>
    /// so the client-side InputFile <c>maxAllowedSize</c>, this constant,
    /// the UI help text, and any future test of the limit can never drift.</summary>
    public const long MaxFileSizeBytes = SlotUploadLimits.MaxFileSizeBytes;

    /// <summary>
    /// Allow-list of file extensions. We don't sniff content, but rejecting
    /// obvious executables (and the misleading "double extension" trick)
    /// blocks the most common upload-bomb patterns.
    /// </summary>
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt",
        ".txt", ".md", ".csv",
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg",
    };

    private readonly IDbContextFactory<ApplicationDbContext> _factory;
    private readonly IUploadPathProvider _paths;
    private readonly IOrgAuthService _orgAuth;
    private readonly ILogger<SlotDocumentService> _log;

    public SlotDocumentService(
        IDbContextFactory<ApplicationDbContext> factory,
        IUploadPathProvider paths,
        IOrgAuthService orgAuth,
        ILogger<SlotDocumentService> log)
    {
        _factory = factory;
        _paths = paths;
        _orgAuth = orgAuth;
        _log = log;
    }

    public async Task<SlotDocument> UploadAsync(
        int slotId,
        Stream content,
        string originalFileName,
        string? contentType,
        long sizeBytes,
        string title,
        string? description,
        string? category,
        string userId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new UnauthorizedAccessException("Sign in required.");
        if (sizeBytes <= 0) throw new ArgumentException("File is empty.", nameof(sizeBytes));
        if (sizeBytes > MaxFileSizeBytes) throw new ArgumentException($"File exceeds {MaxFileSizeBytes / 1024 / 1024} MB limit.", nameof(sizeBytes));
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Title is required.", nameof(title));
        if (string.IsNullOrWhiteSpace(originalFileName)) throw new ArgumentException("File name is required.", nameof(originalFileName));

        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw new ArgumentException($"File type '{ext}' is not allowed. Allowed: {string.Join(", ", AllowedExtensions)}.", nameof(originalFileName));

        if (!await CanManageSlotAsync(slotId, userId, ct))
            throw new UnauthorizedAccessException("You don't have permission to upload to this slot.");

        // Verify the slot exists (FK would catch it, but we want a cleaner error).
        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            var exists = await db.ServiceSlots.AnyAsync(s => s.Id == slotId, ct);
            if (!exists) throw new ArgumentException($"Slot {slotId} not found.", nameof(slotId));
        }

        // Generate a collision-resistant file name. Keep the original extension
        // for content-type / preview purposes; never trust the original name.
        var safeName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var dirAbs = _paths.GetSlotUploadsRoot(slotId);
        Directory.CreateDirectory(dirAbs);
        var fileAbs = Path.Combine(dirAbs, safeName);

        await using (var fs = File.Create(fileAbs))
        {
            await content.CopyToAsync(fs, ct);
        }

        var doc = new SlotDocument
        {
            ServiceSlotId = slotId,
            Title = title.Trim(),
            Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
            FilePath = _paths.GetSlotRelativePath(slotId, safeName),
            OriginalFileName = Path.GetFileName(originalFileName),
            ContentType = contentType,
            SizeBytes = sizeBytes,
            UploadedByUserId = userId,
            UploadedUtc = DateTime.UtcNow,
        };

        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            db.SlotDocuments.Add(doc);
            await db.SaveChangesAsync(ct);
        }

        _log.LogInformation("SlotDocumentService: uploaded {File} ({Size} bytes) to slot {SlotId} by {UserId}.",
            doc.OriginalFileName, doc.SizeBytes, slotId, userId);
        return doc;
    }

    public async Task<SlotDocument> ReplaceAsync(
        int existingDocId,
        Stream content,
        string originalFileName,
        string? contentType,
        long sizeBytes,
        string userId,
        CancellationToken ct = default)
    {
        // Same input validation as UploadAsync — the new file has to pass
        // the same size / extension gates or we're widening the attack
        // surface for "replace" compared to "upload new".
        if (string.IsNullOrWhiteSpace(userId)) throw new UnauthorizedAccessException("Sign in required.");
        if (sizeBytes <= 0) throw new ArgumentException("File is empty.", nameof(sizeBytes));
        if (sizeBytes > MaxFileSizeBytes) throw new ArgumentException($"File exceeds {MaxFileSizeBytes / 1024 / 1024} MB limit.", nameof(sizeBytes));
        if (string.IsNullOrWhiteSpace(originalFileName)) throw new ArgumentException("File name is required.", nameof(originalFileName));

        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw new ArgumentException($"File type '{ext}' is not allowed. Allowed: {string.Join(", ", AllowedExtensions)}.", nameof(originalFileName));

        // Fetch the existing doc + the caller's org role. We need the slot
        // → ministry → org chain to do the permission check, so we Include
        // it here rather than doing a second round-trip.
        SlotDocument doc;
        OrganizationRole? callerRole;
        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            doc = await db.SlotDocuments
                .Include(d => d.ServiceSlot).ThenInclude(s => s.Ministry)
                .FirstOrDefaultAsync(d => d.Id == existingDocId, ct)
                ?? throw new ArgumentException($"Document {existingDocId} not found.");
            callerRole = await _orgAuth.GetRoleAsync(userId, doc.ServiceSlot.Ministry.OrganizationId, ct);
        }

        // Same permission model as DeleteAsync: org admin/coordinator wins;
        // otherwise the original uploader can manage their own upload, but
        // only while they're still a member of the org.
        var isUploader = doc.UploadedByUserId == userId;
        var isOrgAdminOrCoordinator = callerRole is OrganizationRole.Admin or OrganizationRole.Coordinator;
        var isMinistryCoordinator = await CanManageSlotAsync(doc.ServiceSlotId, userId, ct);
        var isCoordinator = isOrgAdminOrCoordinator || isMinistryCoordinator;
        var isMember = callerRole is not null;
        var canReplace = isCoordinator || (isUploader && isMember);
        if (!canReplace)
            throw new UnauthorizedAccessException("You don't have permission to replace this document.");

        // Write the new file FIRST. If this fails the old file and the row
        // stay intact (the user can retry). If we wrote to the row first
        // and the file write failed, the row would point at a non-existent
        // file — a worse failure mode.
        var safeName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var dirAbs = _paths.GetSlotUploadsRoot(doc.ServiceSlotId);
        Directory.CreateDirectory(dirAbs);
        var newFileAbs = Path.Combine(dirAbs, safeName);
        await using (var fs = File.Create(newFileAbs))
        {
            await content.CopyToAsync(fs, ct);
        }

        // Update the row in place — keep the same ID so the download URL
        // (`/slots/{slotId}/documents/{docId}/download`) doesn't change.
        // If SaveChanges throws (e.g. concurrent update, transient SQL
        // error), clean up the new file we just wrote so we don't leave an
        // orphan on disk. The row still points at the old file, so the
        // user sees a failed-replace and the pre-call state is intact.
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct);
            var toUpdate = await db.SlotDocuments.FirstAsync(d => d.Id == existingDocId, ct);
            toUpdate.FilePath = _paths.GetSlotRelativePath(doc.ServiceSlotId, safeName);
            toUpdate.OriginalFileName = Path.GetFileName(originalFileName);
            toUpdate.ContentType = contentType;
            toUpdate.SizeBytes = sizeBytes;
            toUpdate.UploadedByUserId = userId;
            toUpdate.UploadedUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            try { if (File.Exists(newFileAbs)) File.Delete(newFileAbs); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SlotDocumentService: could not clean up new file {File} after row update failed for document {Id}.", newFileAbs, existingDocId);
            }
            throw;
        }

        // Delete the old file. If this fails, the old file is orphaned but
        // the row points at the new one — exactly the same failure mode as
        // DeleteAsync. Log a warning rather than throwing.
        var oldFileAbs = _paths.GetSlotFileAbsolutePath(doc.ServiceSlotId, doc.FilePath);
        if (File.Exists(oldFileAbs))
        {
            try { File.Delete(oldFileAbs); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SlotDocumentService: could not delete old file {File} for document {Id}.", oldFileAbs, existingDocId);
            }
        }

        _log.LogInformation("SlotDocumentService: replaced document {Id} (slot {SlotId}) by {UserId}.", existingDocId, doc.ServiceSlotId, userId);

        // Re-read so the returned object reflects the new file metadata.
        await using var db2 = await _factory.CreateDbContextAsync(ct);
        return await db2.SlotDocuments
            .Include(d => d.UploadedByPerson)
            .FirstAsync(d => d.Id == existingDocId, ct);
    }

    public async Task<List<SlotDocumentGroup>> ListGroupedByCategoryAsync(int slotId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var docs = await db.SlotDocuments
            .AsNoTracking()
            .Include(d => d.UploadedByPerson)
            .Where(d => d.ServiceSlotId == slotId)
            .OrderBy(d => d.Category ?? "\uFFFF")  // uncategorized sorts last
            .ThenByDescending(d => d.UploadedUtc)
            .ToListAsync(ct);

        return docs
            .GroupBy(d => d.Category)
            .Select(g => new SlotDocumentGroup(g.Key, g.ToList()))
            .ToList();
    }

    public async Task<SlotDocument?> GetAsync(int documentId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        // Include ServiceSlot → Ministry so callers (e.g. the download
        // endpoint) can resolve the org without a second round-trip.
        return await db.SlotDocuments
            .AsNoTracking()
            .Include(d => d.ServiceSlot).ThenInclude(s => s.Ministry)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);
    }

    public async Task DeleteAsync(int documentId, string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new UnauthorizedAccessException("Sign in required.");

        SlotDocument doc;
        OrganizationRole? callerRole;
        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            // Include the slot → ministry chain so we can resolve the org
            // and the slot → ministry path in one round-trip.
            doc = await db.SlotDocuments
                .Include(d => d.ServiceSlot).ThenInclude(s => s.Ministry)
                .FirstOrDefaultAsync(d => d.Id == documentId, ct)
                ?? throw new ArgumentException($"Document {documentId} not found.");
            callerRole = await _orgAuth.GetRoleAsync(userId, doc.ServiceSlot.Ministry.OrganizationId, ct);
        }

        var isUploader = doc.UploadedByUserId == userId;
        // Org-level Admin/Coordinator can always delete; otherwise a member
        // who uploaded the doc can still manage their own upload, BUT only
        // while they're still a member (removed users lose delete rights).
        var isOrgAdminOrCoordinator = callerRole is OrganizationRole.Admin or OrganizationRole.Coordinator;
        var isMinistryCoordinator = await CanManageSlotAsync(doc.ServiceSlotId, userId, ct);
        var isCoordinator = isOrgAdminOrCoordinator || isMinistryCoordinator;
        var isMember = callerRole is not null;
        var canDelete = isCoordinator || (isUploader && isMember);
        if (!canDelete)
            throw new UnauthorizedAccessException("You don't have permission to delete this document.");

        // Delete file from disk first, then row. If the file delete fails the
        // row stays (orphaned file but no broken reference); reverse order
        // would leave a broken reference.
        var fileAbs = _paths.GetSlotFileAbsolutePath(doc.ServiceSlotId, doc.FilePath);
        if (File.Exists(fileAbs))
        {
            try { File.Delete(fileAbs); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "SlotDocumentService: could not delete file {File} for document {Id}.", fileAbs, documentId);
            }
        }

        await using (var db = await _factory.CreateDbContextAsync(ct))
        {
            var toDelete = await db.SlotDocuments.FirstAsync(d => d.Id == documentId, ct);
            db.SlotDocuments.Remove(toDelete);
            await db.SaveChangesAsync(ct);
        }

        _log.LogInformation("SlotDocumentService: deleted document {Id} (slot {SlotId}) by {UserId}.",
            documentId, doc.ServiceSlotId, userId);
    }

    public async Task<bool> CanManageSlotAsync(int slotId, string userId, CancellationToken ct = default)
    {
        // Now delegates to the canonical OrgAuthService.CanManageSlotAsync,
        // which also honours the per-slot CoordinatorPersonUserId. The
        // SlotDocumentService-specific wrapper exists only for callers
        // that already have an ISlotDocumentService in hand (the Detail
        // page does) and to keep the param order
        // (slotId-first, userId-second) stable.
        return await _orgAuth.CanManageSlotAsync(userId, slotId, ct);
    }
}
