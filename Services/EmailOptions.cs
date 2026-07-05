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
    /// Default is the PNG-version of the brand mark shipped under
    /// <c>wwwroot/img/</c> — see BRANDING.md for the asset inventory.
    /// Override only if you've re-rasterized the mark and saved it under
    /// a non-default path; the resolver requires the bytes to exist or
    /// the sender degrades to "no inline logo" (warning logged).
    /// </summary>
    public string BrandImagePath { get; set; } = "img/servantsync-mark.png";

    /// <summary>
    /// Content-ID used to wire the cid-attached image into the HTML body.
    /// The HTML references it as <c>cid:{BrandImageContentId}</c>; the
    /// MIME multipart attaches the bytes under the same identifier.
    /// Override only if a downstream integration expects a different id
    /// — the default <c>servantsync-mark</c> is what BRANDING.md commits
    /// to.
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
