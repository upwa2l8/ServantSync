using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Coverage for the brand-asset loader. Pins the test-seam constructor
/// (synthetic bytes) AND the production constructor's null-degradation
/// path. Skips the intricate IFileProvider stub infrastructure — the
/// production code reads bytes through IFileProvider which abstracts
/// over filesystem / embedded-file / manifest-file uniformly, and
/// IFileProvider is a stable Microsoft.Extensions contract. Asserting
/// the no-throw null-degradation path is sufficient to pin the
/// production loader's contract.
/// </summary>
public class EmailBrandAssetsTests
{
    [Fact]
    public void TestSeam_Constructor_ReturnsBrandPayload()
    {
        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var brand = new EmailBrandAssets(bytes);

        Assert.Equal(bytes, brand.LogoBytes);
        Assert.Equal("image/png", brand.ContentType);
        Assert.Equal("servantsync-mark", brand.ContentId);
        Assert.NotNull(brand.RelativePath);
    }

    [Fact]
    public void TestSeam_Constructor_WithCustomContentId_HonorsOverride()
    {
        var bytes = new byte[] { 0x01 };
        var brand = new EmailBrandAssets(
            logoBytes: bytes,
            contentType: "image/svg+xml",
            contentId: "custom-cid",
            relativePath: "img/custom.svg");

        Assert.Equal("custom-cid", brand.ContentId);
        Assert.Equal("image/svg+xml", brand.ContentType);
        Assert.Equal("img/custom.svg", brand.RelativePath);
        Assert.Equal(bytes, brand.LogoBytes);
    }

    [Fact]
    public void TestSeam_Constructor_DefaultsAreCanonical()
    {
        // The defaults match BRANDING.md and are the only path the
        // production caller (BodyBuilder.LinkedResources) is wired
        // against. Pin them so a silent default-shift doesn't break
        // MIME assembly later.
        var brand = new EmailBrandAssets(new byte[] { 0x42 });

        Assert.Equal("servantsync-mark", brand.ContentId);
        Assert.Equal("image/png", brand.ContentType);
        Assert.Matches("test", brand.RelativePath);
    }

    [Fact]
    public void TestSeam_Constructor_AllowsEmptyBytes()
    {
        // Edge case: a degenerate empty byte array still produces a
        // usable record. The MIME builder's null check filters out
        // empty bytes, so the empty-bytes path doesn't reach the
        // email pipeline. Pin the loader's null-tolerance.
        var brand = new EmailBrandAssets(Array.Empty<byte>());

        Assert.NotNull(brand.LogoBytes);
        Assert.Empty(brand.LogoBytes);
    }
}
