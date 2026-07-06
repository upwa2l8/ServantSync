using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ServantSync.Services;

/// <summary>
/// Production IEmailSender backed by MailKit. Reads its connection and
/// sender details from <see cref="EmailOptions"/>. Use this when you have a
/// real SMTP relay (Mailtrap, Amazon SES, Postmark, SendGrid SMTP, etc.).
/// Replaces <see cref="LoggingEmailSender"/> for non-Development environments
/// via DI registration in <c>Program.cs</c>.
///
/// When <see cref="EmailOptions.Smtp.Host"/> is empty, the sender logs a
/// warning and returns Task.CompletedTask — so a misconfigured production
/// environment fails closed instead of throwing per-email during password
/// resets.
///
/// Brand: every email is wrapped in <see cref="EmailBranding.WrapHtmlBody"/>
/// which adds a header band with the cid-attached brand mark and a small
/// footer with the org name + canonical ServantSync URL. The brand image is
/// loaded once at construction via <see cref="IEmailBrandAssets"/> and
/// attached as a related resource under <see cref="EmailBrandAssets.ContentId"/>.
/// If the brand bytes resolve to <c>null</c> (file missing), the
/// <see cref="EmailBranding.WrapHtmlBody"/> helper falls back to a textual
/// brand line ("ServantSync") so the recipient sees nothing has gone wrong
/// — just no mark in the header.
/// </summary>
public class MailKitEmailSender : IEmailSender<IdentityUser>
{
    private readonly EmailOptions _opts;
    private readonly ILogger<MailKitEmailSender> _log;
    private readonly IEmailBrandAssets _brand;

    public MailKitEmailSender(
        IOptions<EmailOptions> opts,
        ILogger<MailKitEmailSender> log,
        IEmailBrandAssets brand)
    {
        _opts = opts.Value;
        _log = log;
        _brand = brand;
    }

    public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink) =>
        SendBrandedAsync(email, "Confirm your ServantSync account",
            "<p>Welcome to ServantSync. Click the link below to confirm your email address:</p>" +
            $"<p style=\"margin:24px 0\"><a class=\"btn\" href=\"{confirmationLink}\">" +
            "Confirm my email</a></p>" +
            "<p>If the button doesn't work, paste this URL into your browser:</p>" +
            $"<p style=\"word-break:break-all;font-size:13px;color:#6b6b6b\">{confirmationLink}</p>");

    public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink) =>
        SendBrandedAsync(email, "Reset your ServantSync password",
            "<p>We received a request to reset your ServantSync password.</p>" +
            "<p>Click the button below to set a new password:</p>" +
            $"<p style=\"margin:24px 0\"><a class=\"btn\" href=\"{resetLink}\">" +
            "Reset my password</a></p>" +
            "<p>This link expires in 24 hours. If you didn't request a reset, " +
            "you can safely ignore this email.</p>" +
            "<p style=\"word-break:break-all;font-size:13px;color:#6b6b6b\">" +
            $"Or paste this URL: {resetLink}</p>");

    public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode) =>
        SendBrandedAsync(email, "Your ServantSync password reset code",
            $"<p>Your ServantSync password reset code is:</p>" +
            $"<p style=\"margin:24px 0;font-size:32px;font-weight:700;letter-spacing:6px;" +
            $"font-family:'Courier New',Courier,monospace;text-align:center;" +
            $"padding:16px;background:#f5f5fa;border-radius:8px\">" +
            $"{System.Net.WebUtility.HtmlEncode(resetCode)}</p>" +
            "<p>Enter this code on the password reset page to continue. " +
            "It expires in a few minutes.</p>");

    public Task SendTwoFactorCodeAsync(IdentityUser user, string email, string code) =>
        SendBrandedAsync(email, "Your ServantSync two-factor authentication code",
            $"<p>Your ServantSync two-factor authentication code is:</p>" +
            $"<p style=\"margin:24px 0;font-size:32px;font-weight:700;letter-spacing:6px;" +
            $"font-family:'Courier New',Courier,monospace;text-align:center;" +
            $"padding:16px;background:#f5f5fa;border-radius:8px\">" +
            $"{System.Net.WebUtility.HtmlEncode(code)}</p>" +
            "<p>This code expires in a few minutes.</p>");

    private async Task SendBrandedAsync(string to, string subject, string innerHtml)
    {
        if (string.IsNullOrWhiteSpace(_opts.Smtp.Host))
        {
            _log.LogWarning(
                "MailKitEmailSender: SMTP host is not configured; dropping {Subject} to {Email}.",
                subject, to);
            return;
        }

        var message = BuildMessage(_opts, _brand, to, subject, innerHtml);

        using var client = new SmtpClient();
        try
        {
            var tlsMode = ParseTlsMode(_opts.Smtp.TlsMode);
            await client.ConnectAsync(_opts.Smtp.Host, _opts.Smtp.Port, tlsMode);

            if (!string.IsNullOrEmpty(_opts.Smtp.User))
            {
                var pw = _opts.Smtp.Password ?? string.Empty;
                await client.AuthenticateAsync(_opts.Smtp.User, pw);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(quit: true);

            _log.LogInformation("MailKitEmailSender: sent {Subject} to {Email}.", subject, to);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MailKitEmailSender: failed to send {Subject} to {Email}.", subject, to);
            throw;
        }
    }

    /// <summary>
    /// Pure builder for the MIME message. <c>public static</c> so the
    /// unit tests in
    /// <c>tests/ServantSync.Tests/MailKitEmailSenderTests.cs</c> can
    /// drive it with a stub <see cref="IEmailBrandAssets"/> and assert
    /// the resulting <see cref="MimeMessage"/>'s shape without ever
    /// touching the SMTP client. We choose <c>public</c> over
    /// <c>internal + InternalsVisibleTo</c> because the helper has
    /// clean inputs and no side-effects — exposing it costs nothing
    /// and avoids the <c>csproj</c> plumbing. See BRANDING.md for the
    /// email-content design tokens behind this builder.
    /// </summary>
    public static MimeMessage BuildMessage(
        EmailOptions opts,
        IEmailBrandAssets brand,
        string to,
        string subject,
        string innerHtml)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(opts.FromName, opts.FromAddress));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;

        // Wrap the inner body in the brand shell BEFORE assembling the
        // BodyBuilder — that way the builder sees the final rendered
        // HTML when adding LinkedResources, which means the cid URL in
        // the HTML matches the resource we're about to attach.
        var htmlBody = EmailBranding.WrapHtmlBody(brand, opts.FromName, subject, innerHtml);
        var textBody = EmailBranding.WrapTextBody(opts.FromName, StripHtml(innerHtml));

        var builder = new BodyBuilder
        {
            HtmlBody = htmlBody,
            TextBody = textBody,
        };

        if (brand.LogoBytes is not null && brand.LogoBytes.Length > 0)
        {
            // MimeKit's LinkedResourceCollection.Add overload that takes
            // bytes + content-type is positional — no `content:` /
            // `contentId:` named parameters. The Content-Id header (the
            // `<cid>` value that `<img src="cid:...">` resolves to inside
            // the HTML body) is set on the returned LinkedResource via
            // its ContentId property; MimeKit renders it with the
            // mandatory angle brackets per RFC 2392 ourselves if we set
            // it via Headers.
            var contentType = MimeKit.ContentType.Parse(brand.ContentType);
            var image = builder.LinkedResources.Add(
                "servantsync-mark.png",
                brand.LogoBytes,
                contentType);
            // Use the strongly-typed ContentId setter when present; fall
            // back to the raw header if the version we compiled against
            // doesn't expose it.
            image.Headers["Content-Id"] = brand.ContentId;

            // The string-accepting ContentDisposition ctor avoids the
            // MimeKit.DispositionType namespace-resolution surprise —
            // "inline" / "attachment" / "form-data" are stable wire
            // values per RFC 2183.
            image.ContentDisposition = new MimeKit.ContentDisposition("inline")
            {
                FileName = "servantsync-mark.png",
            };
        }

        message.Body = builder.ToMessageBody();
        return message;
    }

    private static SecureSocketOptions ParseTlsMode(string? mode) => (mode ?? "").Trim().ToLowerInvariant() switch
    {
        "none"                   => SecureSocketOptions.None,
        "ssl" or "sslconnect" or "ssl_on_connect" => SecureSocketOptions.SslOnConnect,
        "starttls"               => SecureSocketOptions.StartTls,
        "" or "starttlswhenavailable" => SecureSocketOptions.StartTlsWhenAvailable,
        _                        => SecureSocketOptions.StartTlsWhenAvailable,
    };

    /// <summary>
    /// Produces a degraded plain-text fallback for email clients that refuse
    /// HTML. Strips tags crudely — sufficient for our short admin messages.
    /// </summary>
    private static string StripHtml(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var noTags = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");
        return System.Net.WebUtility.HtmlDecode(noTags).Trim();
    }
}

/// <summary>
/// Pure helper: produces the HTML shell + plain-text shell for branded
/// emails. Lives next to <see cref="MailKitEmailSender"/> because there's
/// exactly one consumer right now; if a second placeholder wants to use
/// the same shell (e.g. an admin broadcast service), promote to its own
/// file.
///
/// Design choices to keep in mind while editing:
/// <list type="bullet">
/// <item><description>OUTLOOK uses Word for rendering, NOT a browser.
/// <c>&lt;div&gt;</c> + CSS-grid works fine in Gmail but Outlook refuses
/// every non-table layout. The header / footer are 100% nested
/// <c>&lt;table&gt;</c> elements — Outlook-paranoid.</description></item>
/// <item><description><c>cid:</c> URLs are not stylesheet-designable.
/// The brand mark is referenced inline as
/// <c>&lt;img src="cid:{ContentId}" width="120"&gt;</c>; CSS sizing
/// doesn't reach an embedded cid image in Outlook.</description></item>
/// <item><description>Cell-padding / border-spacing is set on the
/// <c>&lt;table&gt;</c> element directly rather than via CSS — Outlook
/// ignores CSS border-spacing.</description></item>
/// <item><description>No JS, no remote resource, no external CSS.
/// The style block is inline so it works behind email-client proxy
/// parsers that strip <c>&lt;head&gt;</c>.</description></item>
/// </list>
/// </summary>
internal static class EmailBranding
{
    /// <summary>
    /// Wrap <paramref name="innerHtml"/> in the brand shell. The header
    /// band carries the cid-attached mark IF the brand asset has bytes
    /// (i.e. <see cref="EmailOptions.BrandImagePath"/> is set to a
    /// resolvable file); otherwise the typographic marketing wordmark
    /// is rendered as styled text in the brand purple. The typographic
    /// text is the new default — see BRANDING.md "Email brand" for the
    /// rationale (the marketing wordmark is text by design, so a text
    /// representation is the canonical email-friendly form). The footer
    /// carries the org name only; the breadth tagline moved up under
    /// the wordmark in the header band.
    /// </summary>
    public static string WrapHtmlBody(IEmailBrandAssets brand, string orgName, string subject, string innerHtml)
    {
        // Brand line: image when bytes present, typographic text
        // otherwise. We don't conditionally emit <img> with a broken
        // src — many clients flag a broken-image placeholder as
        // suspicious. The text fallback is the new default (empty
        // BrandImagePath); the cid image is an override for
        // deployments that want a visual mark.
        var brandLine = brand.LogoBytes is { Length: > 0 }
            ? $"<img src=\"cid:{brand.ContentId}\" alt=\"{System.Net.WebUtility.HtmlEncode(orgName)}\" width=\"120\" height=\"120\" style=\"display:block;border:0;outline:none;text-decoration:none\">"
            : $"<div style=\"font-size:28px;font-weight:800;letter-spacing:-0.5px;color:#3730a3;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;-ms-text-size-adjust:100%;-webkit-text-size-adjust:100%\">ServantSync</div>" +
              $"<div style=\"font-size:14px;font-weight:400;color:#6b6b6b;margin-top:4px;line-height:1.4\">Volunteer coordination, in sync.</div>";

        // Outlook-friendly nested-table layout. Inline styles only so the
        // email survives Gmail's stripping of <head>/<style> blocks.
        return $@"<!doctype html>
<html lang=""en"">
<head>
  <meta charset=""utf-8"">
  <meta name=""viewport"" content=""width=device-width,initial-scale=1"">
  <title>{System.Net.WebUtility.HtmlEncode(subject)}</title>
</head>
<body style=""margin:0;padding:0;background:#ffffff;color:#1f1f2e;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:15px;line-height:1.5"">
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background:#ffffff"">
    <tr>
      <td align=""center"" style=""padding:32px 16px"">
        <table role=""presentation"" width=""560"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""max-width:560px;width:100%"">
          <tr>
            <td align=""left"" style=""padding:0 0 24px 0"">
              {brandLine}
            </td>
          </tr>
          <tr>
            <td style=""padding:0;border-top:1px solid #e6e6f0"">
              &nbsp;
            </td>
          </tr>
          <tr>
            <td style=""padding:24px 0;color:#1f1f2e"">
              {innerHtml}
            </td>
          </tr>
          <tr>
            <td style=""padding:24px 0 0 0;border-top:1px solid #e6e6f0;font-size:13px;color:#6b6b6b"">
              <strong>{System.Net.WebUtility.HtmlEncode(orgName)}</strong>
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>";
    }

    /// <summary>
    /// Plain-text counterpart of <see cref="WrapHtmlBody"/>. Email clients
    /// that disable HTML (or are alt-text-only) fall back to this. No
    /// image is referenced because cid images have no plain-text
    /// representation — we surface the org name in the body so the
    /// text-only reader can still identify the sender.
    /// </summary>
    public static string WrapTextBody(string orgName, string innerText)
    {
        var banner = $"----  {orgName}  ----";
        return $"{banner}\n\n{innerText}\n\n{banner}\n";
    }
}
