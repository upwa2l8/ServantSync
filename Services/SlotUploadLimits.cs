namespace ServantSync.Services;

/// <summary>
/// Single source of truth for slot-document upload limits. The user-facing
/// label ("10 MB") and the byte cap are kept here so the client-side
/// InputFile <c>maxAllowedSize</c>, the server-side validation in
/// <see cref="SlotDocumentService"/>, the UI help text, and any future
/// test that exercises the limit can never drift apart.
/// </summary>
public static class SlotUploadLimits
{
    /// <summary>Hard cap on a single upload, in bytes.</summary>
    public const long MaxFileSizeBytes = 10L * 1024 * 1024;

    /// <summary>Human-readable label that always matches <see cref="MaxFileSizeBytes"/>.</summary>
    public const string MaxFileSizeDisplay = "10 MB";
}
