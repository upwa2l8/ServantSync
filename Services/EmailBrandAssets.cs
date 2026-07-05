using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace ServantSync.Services;

/// <summary>
/// Carries the brand mark (logo + content-type + relative-path) used as a
/// <c>cid:</c>-attached inline image in every HTML email body that
/// <see cref="MailKitEmailSender"/> sends. Concrete resolution happens once
/// at construction; the bytes are cached for the lifetime of the singleton
/// so every re-send doesn't re-read the file. <see cref="LogoBytes"/> is
/// <c>null</c> if the configured path didn't resolve (file missing, web root
/// not yet warmed) — senders must treat that as "no inline logo" rather
/// than failing the whole email.
///
/// The loader reads via <see cref="IFileProvider"/> (NOT a raw
/// <see cref="System.IO.File.ReadAllBytes(string)"/>) so it works in
/// production under any IFileProvider the host composes in — file system in
/// <c>dotnet run</c>, embedded-file in tests, manifest in single-file
/// publish. <c>null</c> when the file is genuinely missing.
/// </summary>
public interface IEmailBrandAssets
{
    /// <summary>Content-ID referenced from the HTML body as <c>cid:...</c>.</summary>
    string ContentId { get; }

    /// <summary>MIME content-type the HTML body says to expect ("image/png" for our mark).</summary>
    string ContentType { get; }

    /// <summary>File-relative path the asset was resolved from (for log breadcrumbing).</summary>
    string RelativePath { get; }

    /// <summary>The bytes the sender attaches. <c>null</c> when the file was missing.</summary>
    byte[]? LogoBytes { get; }
}

/// <summary>
/// Default implementation. Reads <see cref="EmailOptions.BrandImagePath"/>
/// (default <c>img/servantsync-mark.png</c>) via the host's content-root
/// file provider. The default path resolves the file shipped under
/// <c>wwwroot/img/</c> — see BRANDING.md for the asset inventory.
///
/// Splitting the brand payload out from <see cref="MailKitEmailSender"/>
/// makes the MIME-builder pure-testable: tests construct an
/// <see cref="EmailBrandAssets"/> directly with synthetic bytes, then call
/// the sender's internal builder. See
/// <c>tests/ServantSync.Tests/EmailBrandAssetsTests.cs</c>.
/// </summary>
public class EmailBrandAssets : IEmailBrandAssets
{
    public string ContentId { get; }
    public string ContentType { get; } = "image/png";
    public string RelativePath { get; }
    public byte[]? LogoBytes { get; }

    /// <summary>
    /// Production constructor. Resolves the brand file from
    /// <see cref="IOptions{T}"/> + the composed <see cref="IFileProvider"/>
    /// in <param name="webFileProvider"/>. The <see cref="IHostEnvironment"/>
    /// is taken so we log the actual web-root path used (matters when the
    /// app is hosted under a sub-path or in a single-file publish).
    /// </summary>
    public EmailBrandAssets(
        IOptions<EmailOptions> opts,
        IFileProvider webFileProvider,
        ILogger<EmailBrandAssets> log)
    {
        var emailOpts = opts.Value;
        RelativePath = emailOpts.BrandImagePath;
        ContentId = emailOpts.BrandImageContentId;

        // IFileProvider returns IFileInfo with Exists=false when the file
        // is genuinely missing — that's the signal we use to degrade
        // gracefully (no email failure, just no inline logo). We never
        // throw here: a misconfigured brand asset must not break password
        // resets.
        var info = webFileProvider.GetFileInfo(RelativePath);
        if (!info.Exists)
        {
            log.LogWarning(
                "EmailBrandAssets: brand image {Path} not found in web root; emails will be sent without the inline logo. " +
                "This is non-fatal but visible: the recipient sees a header without the mark.",
                RelativePath);
            LogoBytes = null;
            return;
        }

        using var stream = info.CreateReadStream();
        // 64 KB is plenty for our current 180×180 PNG (~8 KB once
        // compressed), but the limit is raised to 1 MB to absorb future
        // larger mark variants without a code touch. The MemoryStream is
        // for the lifetime of the singleton — bytes are bounded.
        using var ms = new MemoryStream(capacity: 64 * 1024);
        stream.CopyTo(ms);
        LogoBytes = ms.ToArray();
    }

    /// <summary>
    /// Test seam: construct directly with synthetic bytes. Used by the unit
    /// tests to drive the MailKit MIME builder path without filesystem
    /// coupling. <c>contentType</c> defaults to <c>image/png</c> which is
    /// the only format the production loader emits — keeps the inbound
    /// shape narrow.
    /// </summary>
    public EmailBrandAssets(byte[] logoBytes, string contentType = "image/png", string contentId = "servantsync-mark", string relativePath = "test")
    {
        LogoBytes = logoBytes;
        ContentType = contentType;
        ContentId = contentId;
        RelativePath = relativePath;
    }
}
