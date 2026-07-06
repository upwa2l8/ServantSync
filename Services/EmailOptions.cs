namespace ServantSync.Services;

/// <summary>
/// Bound from the <c>Email</c> section of <c>appsettings.json</c>. Drives
/// <see cref="MailKitEmailSender"/>. If <see cref="SmtpOptions.Host"/> is
/// blank, the sender logs and drops the message — so a misconfigured
/// production environment fails closed rather than sending to the wrong
/// server or throwing.
/// </summary>
public class EmailOptions
{
    public SmtpOptions Smtp { get; set; } = new();

    public string FromAddress { get; set; } = "noreply@servantsync.local";

    public string FromName { get; set; } = "ServantSync";

    /// <summary>
    /// File-relative path under the web root pointing at the brand mark
    /// used as the cid-attached inline image in every branded email body.
    /// Default is <c>""</c> (empty) — the email uses the typographic
    /// text wordmark in the header band instead of a rasterized image.
    /// The marketing wordmark is a typographic lockup by design (see
    /// BRANDING.md "Two assets, two roles"), so text is the canonical
    /// representation: it survives every email client including
    /// Outlook's Word-based renderer, requires no PNG generation tool,
    /// and carries the brand without depending on a cid image that some
    /// clients block.
    ///
    /// Set this to a path under <c>wwwroot/</c> (e.g.
    /// <c>"img/servantsync-marketing.png"</c>) to re-enable the cid
    /// image path; the resolver will read it via
    /// <see cref="Microsoft.Extensions.FileProviders.IFileProvider"/> and
    /// fall back to text if the file is missing. The cid image is
    /// preserved as an override for deployments that want a visual mark
    /// in the email header.
    /// </summary>
    public string BrandImagePath { get; set; } = "";

    /// <summary>
    /// Content-ID used to wire the cid-attached image into the HTML body
    /// (only when <see cref="BrandImagePath"/> is non-empty and resolves
    /// to a readable file). The HTML references it as
    /// <c>cid:{BrandImageContentId}</c>; the MIME multipart attaches
    /// the bytes under the same identifier. Default
    /// <c>servantsync-mark</c> matches the pre-text-default contract
    /// from BRANDING.md; override only if a downstream integration
    /// expects a different id.
    /// </summary>
    public string BrandImageContentId { get; set; } = "servantsync-mark";
}

public class SmtpOptions
{
    /// <summary>SMTP relay hostname. Empty = drop to log (dev-like fallback).</summary>
    public string Host { get; set; } = "";

    /// <summary>SMTP port. 587 (submission) is the typical default for STARTTLS.</summary>
    public int Port { get; set; } = 587;

    public string? User { get; set; }

    public string? Password { get; set; }

    /// <summary>
    /// One of "None", "StartTls", "StartTlsWhenAvailable" (default),
    /// or "SslOnConnect". Parsed case-insensitively in the MailKit sender.
    /// </summary>
    public string TlsMode { get; set; } = "StartTlsWhenAvailable";
}
