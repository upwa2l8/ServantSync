using MimeKit;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// MIME-assembly tests for <see cref="MailKitEmailSender"/>. The
/// production surface area here is just the
/// <see cref="MailKitEmailSender.BuildMessage"/> helper — no SMTP
/// connection, no real relay, no network. These tests pin the highest-
/// value invariants of the message that
/// <c>MailKit.SmtpClient.SendAsync</c> receives in production:
/// subject + sender/recipient preservation, HTML body containing the
/// cid reference + inner content, plain-text alt body present, and
/// graceful degradation when brand bytes are absent.
///
/// What we don't pin here: the internal multipart layout (related vs
/// alternative vs mixed), the precise nesting of HTML vs image, or
/// the disposition type's underlying enum/string split. Those are
/// MimeKit-version-specific and are covered indirectly by these
/// top-level invariants — a refactor that breaks the cid resolution
/// or the plain-text alt body would also break these tests.
/// </summary>
public class MailKitEmailSenderTests
{
    private static readonly byte[] FakeBrandBytes = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x01
    };

    private static EmailOptions DefaultOptions() => new()
    {
        FromAddress = "noreply@servantsync.test",
        FromName = "ServantSync Test",
    };

    [Fact]
    public void BuildMessage_SetsSubject_From_AndTo()
    {
        var brand = new EmailBrandAssets(FakeBrandBytes);
        var msg = MailKitEmailSender.BuildMessage(
            DefaultOptions(), brand, "alice@test", "Reset your password",
            "<p>Hi</p>");

        Assert.Equal("Reset your password", msg.Subject);
        Assert.Single(msg.From);
        // MimeKit.InternetAddress exposes .Name on the base class but
        // .Address only on MailboxAddress. The from/to fields on
        // MimeMessage are typed as InternetAddress so the compiler
        // refused .Address access — falling back to .ToString() keeps
        // the assertion stable across MimeKit versions.
        var fromStr = msg.From[0].ToString();
        Assert.Contains("ServantSync Test", fromStr);
        Assert.Contains("noreply@servantsync.test", fromStr);
        Assert.Single(msg.To);
        Assert.Contains("alice@test", msg.To[0].ToString());
    }

    [Fact]
    public void BuildMessage_HtmlBody_ReferencesCidImage_WhenBrandPresent()
    {
        var brand = new EmailBrandAssets(FakeBrandBytes, contentId: "servantsync-mark");
        var innerHtml = "<p>Verify your email</p>";
        var msg = MailKitEmailSender.BuildMessage(
            DefaultOptions(), brand, "alice@test", "Test", innerHtml);

        var htmlText = msg.HtmlBody;
        Assert.NotNull(htmlText);
        Assert.Contains("cid:servantsync-mark", htmlText);
        Assert.Contains(innerHtml, htmlText);
    }

    [Fact]
    public void BuildMessage_HtmlBody_DoesNotReferenceCid_WhenBrandAbsent()
    {
        var brand = new EmailBrandAssets(
            logoBytes: null!,
            contentType: "image/png",
            contentId: "servantsync-mark",
            relativePath: "test");

        var msg = MailKitEmailSender.BuildMessage(
            DefaultOptions(), brand, "alice@test", "Test", "<p>Hi</p>");

        // No cid reference but the HTML body should still be
        // assembled (with a textual wordmark in the header).
        var htmlText = msg.HtmlBody;
        Assert.NotNull(htmlText);
        Assert.DoesNotContain("cid:servantsync-mark", htmlText);
        // The wordmark line in the header (closing </div> matched
        // so the assertion can't be satisfied by the org-name
        // substring in the footer — e.g. "ServantSync Test").
        Assert.Contains(">ServantSync</div>", htmlText);
    }

    [Fact]
    public void BuildMessage_HtmlBody_RendersTextWordmark_WhenBrandAbsent()
    {
        // Round-EMAIL-BRAND: the default brand path is empty (text
        // wordmark, no cid image). The marketing wordmark is
        // typographic by design — see BRANDING.md "Email brand" —
        // so a styled-text "ServantSync" in brand-purple IS the
        // wordmark and survives every email client including
        // Outlook's Word-based renderer.
        var brand = new EmailBrandAssets(
            logoBytes: null!,
            contentType: "image/png",
            contentId: "servantsync-mark",
            relativePath: "test");

        var msg = MailKitEmailSender.BuildMessage(
            DefaultOptions(), brand, "alice@test", "Test", "<p>Hi</p>");

        var htmlText = msg.HtmlBody;
        Assert.NotNull(htmlText);
        // Wordmark text is present.
        Assert.Contains(">ServantSync<", htmlText);
        // Brand-purple color from the coolSide / 0% token (#3730a3).
        Assert.Contains("#3730a3", htmlText);
        // Breadth tagline moved up under the wordmark in the header.
        Assert.Contains("Volunteer coordination, in sync.", htmlText);
        // No cid reference (text path only).
        Assert.DoesNotContain("cid:", htmlText);
    }

    [Fact]
    public void BuildMessage_TextBody_OmitsWordmarkMarkup_WhenBrandAbsent()
    {
        // The text-only alt body must NOT carry the HTML wordmark
        // markup (no <div style=...> leaking into the text part) and
        // must NOT carry the tagline as styled-HTML (the tagline is
        // part of the brand line, not a body sentence). The stripper
        // produces a clean text body with the org name and a banner
        // — that's all the text-mode reader needs.
        var brand = new EmailBrandAssets(
            logoBytes: null!,
            contentType: "image/png",
            contentId: "servantsync-mark",
            relativePath: "test");

        var msg = MailKitEmailSender.BuildMessage(
            DefaultOptions(), brand, "alice@test", "Test", "<p>Hi</p>");

        var textBody = msg.TextBody;
        Assert.NotNull(textBody);
        // No HTML tag survivors.
        Assert.DoesNotContain("<div", textBody);
        Assert.DoesNotContain("#3730a3", textBody);
        // Org name still appears (it's the banner source).
        Assert.Contains("ServantSync Test", textBody!);
    }

    [Fact]
    public void BuildMessage_Html_UsesOutlookSafeTableLayout()
    {
        var brand = new EmailBrandAssets(FakeBrandBytes);
        var msg = MailKitEmailSender.BuildMessage(
            DefaultOptions(), brand, "alice@test", "Test", "<p>Hi</p>");

        // Outlook (Word-based renderer) refuses anything that's not a
        // table-based layout. Pin the wrapper structure here so a
        // refactor that drifts toward div-based layout is caught.
        var htmlText = msg.HtmlBody;
        Assert.NotNull(htmlText);
        Assert.Contains("<table role=\"presentation\"", htmlText);
        Assert.Contains("<!doctype html>", htmlText);
    }

    [Fact]
    public void BuildMessage_TextBody_IsPresent_AndStripsTags()
    {
        var brand = new EmailBrandAssets(FakeBrandBytes);
        var msg = MailKitEmailSender.BuildMessage(
            DefaultOptions(), brand, "alice@test", "Test",
            "<p>Your code is <b>123456</b></p>");

        // MimeMessage.TextBody returns the plain-text version of the
        // body when BodyBuilder sets both HtmlBody + TextBody. It
        // strips tags crudely — sufficient confirmation that the body
        // builder alternates both formats.
        var textBody = msg.TextBody;
        Assert.NotNull(textBody);
        Assert.Contains("123456", textBody!);
        // No HTML tag survivors in the plain-text body.
        Assert.DoesNotContain("<b>", textBody);
    }

    [Fact]
    public void BuildMessage_DegradedHtml_SubjectHtmlEncodes()
    {
        // Trust boundary: subject (which appears in <title>) MUST be
        // HTML-encoded so a hostile subject can't inject script via
        // `<` `>`. Without this, an attacker who controls the
        // subject line (untrusted caller-supplied copy) could break
        // out of the <title> and inject arbitrary HTML.
        var brand = new EmailBrandAssets(logoBytes: null!);
        var msg = MailKitEmailSender.BuildMessage(
            DefaultOptions(), brand, "alice@test",
            subject: "Reset your <ServantSync> password",
            innerHtml: "<p>Hi & welcome</p>");

        var htmlText = msg.HtmlBody;
        Assert.NotNull(htmlText);
        Assert.Contains("&lt;ServantSync&gt;", htmlText);
        Assert.Contains("Hi", htmlText);
    }

    [Fact]
    public void BuildMessage_BodyContainsImageMimetype_WhenBrandPresent()
    {
        // Walks the body tree for any MimePart with image/* content
        // type — the `BodyBuilder.LinkedResources.Add` path produces
        // such a part inside the `multipart/related` envelope. We
        // don't decompose the multipart (which varies by MimeKit
        // version) but we do verify the brand bytes were routed
        // somewhere in the body. The walker itself filters by
        // MediaType.StartsWith("image") so an additional Subtype
        // assertion isn't necessary — and `ContentType.Subtype`
        // access fails on some MimeKit versions, so we deliberately
        // avoid the cross-version API drift here.
        var brand = new EmailBrandAssets(FakeBrandBytes);
        var msg = MailKitEmailSender.BuildMessage(
            DefaultOptions(), brand, "alice@test", "Test", "<p>Hi</p>");

        var image = FindMimePartWithMediaType(msg.Body, "image");
        Assert.NotNull(image);
    }

    [Fact]
    public void BuildMessage_BodyHasNoImageMimetype_WhenBrandAbsent()
    {
        // Mirrors the above test: when brand bytes are absent, the
        // body tree must NOT contain any image/* MimePart. (No
        // LinkedResource is added because the production MailKit
        // wrapper gates the Add call on `LogoBytes.Length > 0`.)
        var brand = new EmailBrandAssets(
            logoBytes: null!,
            contentType: "image/png",
            contentId: "servantsync-mark",
            relativePath: "test");
        var msg = MailKitEmailSender.BuildMessage(
            DefaultOptions(), brand, "alice@test", "Test", "<p>Hi</p>");

        var image = FindMimePartWithMediaType(msg.Body, "image");
        Assert.Null(image);
    }

    /// <summary>
    /// Recursively digs through a MimeKit body looking for any
    /// <see cref="MimePart"/> whose <see cref="MimeKit.ContentType.MediaType"/>
    /// matches the requested prefix (e.g. "image" matches
    /// "image/png", "image/jpeg", etc.). Walks both Multipart and
    /// nested-Multipart paths.
    /// </summary>
    private static MimePart? FindMimePartWithMediaType(MimeEntity? body, string mediaTypePrefix)
    {
        if (body is null) return null;
        if (body is MimePart part &&
            part.ContentType.MediaType.StartsWith(mediaTypePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return part;
        }
        if (body is Multipart multi)
        {
            foreach (var child in multi)
            {
                var hit = FindMimePartWithMediaType(child, mediaTypePrefix);
                if (hit is not null) return hit;
            }
        }
        return null;
    }
}
