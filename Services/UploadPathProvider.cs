namespace ServantSync.Services;

public interface IUploadPathProvider
{
    /// <summary>Absolute path to the directory where training media files are stored on disk.</summary>
    string TrainingUploadsRoot { get; }

    /// <summary>Resolves an absolute path under the training uploads directory given a file name.</summary>
    string GetTrainingUploadPath(string fileName);

    /// <summary>Absolute path to the directory where slot-document files are stored for a given slot.</summary>
    string GetSlotUploadsRoot(int slotId);

    /// <summary>
    /// Returns the relative path (under wwwroot) used to store a file in
    /// <see cref="SlotDocument.FilePath"/>, e.g. "uploads/slots/42/abc.pdf".
    /// </summary>
    string GetSlotRelativePath(int slotId, string fileName);

    /// <summary>
    /// Resolves a relative path stored in <see cref="SlotDocument.FilePath"/>
    /// back to an absolute on-disk path. Refuses paths that try to escape the
    /// slot's upload directory (path-traversal check).
    /// </summary>
    string GetSlotFileAbsolutePath(int slotId, string relativePath);
}

public class UploadPathProvider : IUploadPathProvider
{
    private readonly IWebHostEnvironment _env;

    public UploadPathProvider(IWebHostEnvironment env)
    {
        _env = env;
        TrainingUploadsRoot = Path.Combine(_env.WebRootPath, "uploads", "training");
        Directory.CreateDirectory(TrainingUploadsRoot);
    }

    public string TrainingUploadsRoot { get; }

    public string GetTrainingUploadPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));

        // Strip any path-traversal attempts and combine safely.
        var safe = Path.GetFileName(fileName);
        return Path.Combine(TrainingUploadsRoot, safe);
    }

    public string GetSlotUploadsRoot(int slotId)
    {
        var root = Path.Combine(_env.WebRootPath, "uploads", "slots", slotId.ToString());
        Directory.CreateDirectory(root);
        return root;
    }

    public string GetSlotRelativePath(int slotId, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name is required.", nameof(fileName));
        // Strip any path-traversal attempts; only the leaf name is allowed.
        var safe = Path.GetFileName(fileName);
        return Path.Combine("uploads", "slots", slotId.ToString(), safe)
            .Replace('\\', '/');
    }

    public string GetSlotFileAbsolutePath(int slotId, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("Relative path is required.", nameof(relativePath));

        // Reject absolute paths and any path that tries to escape the slot
        // directory (e.g. via ".."). Compare directories, not prefixes —
        // otherwise uploads/slots/420/x.pdf would slip through a check
        // against uploads/slots/42 (sibling-directory bypass).
        var root = GetSlotUploadsRoot(slotId);
        var fullPath = Path.GetFullPath(Path.Combine(_env.WebRootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var fullPathDir = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var normalizedRoot = Path.GetFullPath(root);
        if (!string.Equals(fullPathDir, normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid file path.");
        return fullPath;
    }
}
