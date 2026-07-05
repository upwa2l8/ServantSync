using Microsoft.EntityFrameworkCore;
using ServantSync.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Unit tests for <see cref="PdfPageCountHealer"/>. These lock in the
/// round-AI contract: a volunteer's Take page can self-heal a PDF's
/// TotalPageCount when the original upload left it null. The trust
/// boundary (PdfPig exception surfaces as "still unknown") is also
/// tested implicitly by the "missing-file" case (File.Exists false),
/// which the healer treats identically to a PdfPig fail.
/// </summary>
public class PdfPageCountHealerTests : SqliteTestBase
{
    [Fact]
    public async Task TryHeal_NullTotalPageCount_OnLocalOpenablePdf_PopulatesAndReturns()
    {
        var root = NewTempDir();
        try
        {
            var path = Path.Combine(root, "guide.pdf");
            WritePdf(path, 3);

            var org = TestData.Org(Factory, "Org A");
            var content = TestData.PdfContent(Factory, org.Id, "Guide", totalPages: null);
            await using (var db = await Factory.CreateDbContextAsync())
            {
                var row = await db.TrainingContents.FindAsync(content.Id);
                row!.FilePathOrUrl = "/uploads/training/guide.pdf";
                await db.SaveChangesAsync();
            }

            var sut = new PdfPageCountHealer(Factory, new StubUploadPathProvider(root));
            var result = await sut.TryHealAsync(content.Id);

            Assert.Equal(3, result);
            await using var db2 = await Factory.CreateDbContextAsync();
            var reloaded = await db2.TrainingContents.FindAsync(content.Id);
            Assert.Equal(3, reloaded!.TotalPageCount);
            // Version unchanged — heal is metadata correction, not a
            // content refresh. Activities keyed to the old version stay.
            Assert.Equal(content.Version, reloaded.Version);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TryHeal_AlreadyHasTotalPageCount_IsNoOp()
    {
        var root = NewTempDir();
        try
        {
            var path = Path.Combine(root, "guide.pdf");
            WritePdf(path, 7);

            var org = TestData.Org(Factory, "Org A");
            var content = TestData.PdfContent(Factory, org.Id, "Guide", totalPages: 7);
            await using (var db = await Factory.CreateDbContextAsync())
            {
                var row = await db.TrainingContents.FindAsync(content.Id);
                row!.FilePathOrUrl = "/uploads/training/guide.pdf";
                await db.SaveChangesAsync();
            }

        var sut = new PdfPageCountHealer(Factory, new StubUploadPathProvider(root));
        var result = await sut.TryHealAsync(content.Id);

        Assert.Equal(7, result);            // returns the existing count
        await using var db2 = await Factory.CreateDbContextAsync();
        var reloaded = await db2.TrainingContents.FindAsync(content.Id);
        Assert.Equal(7, reloaded!.TotalPageCount);
        Assert.Equal(content.Version, reloaded.Version);
        // Path must be exactly what we set above — the no-op heal
        // must NOT have rewritten or nulled it. (We can't assert
        // against `content.FilePathOrUrl` here because `content` is
        // a stale in-memory snapshot from TestData.PdfContent's
        // default path; the override above set the real DB value.)
        Assert.Equal("/uploads/training/guide.pdf", reloaded!.FilePathOrUrl);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TryHeal_PdfMissingOnDisk_ReturnsNull_NoRowUpdate()
    {
        var root = NewTempDir();
        try
        {
            // No file written — PdfPageCounter would throw
            // FileNotFoundException; we want this to be treated the
            // same as a PdfPig parsing failure (return null, no write).
            var org = TestData.Org(Factory, "Org A");
            var content = TestData.PdfContent(Factory, org.Id, "Guide", totalPages: null);
            await using (var db = await Factory.CreateDbContextAsync())
            {
                var row = await db.TrainingContents.FindAsync(content.Id);
                row!.FilePathOrUrl = "/uploads/training/missing.pdf";
                await db.SaveChangesAsync();
            }

            var sut = new PdfPageCountHealer(Factory, new StubUploadPathProvider(root));
            var result = await sut.TryHealAsync(content.Id);

            Assert.Null(result);
            await using var db2 = await Factory.CreateDbContextAsync();
            var reloaded = await db2.TrainingContents.FindAsync(content.Id);
            Assert.Null(reloaded!.TotalPageCount);
            Assert.Equal(content.Version, reloaded.Version);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TryHeal_HealBumpsVersionExpectation_NotBumped()
    {
        // Defense-in-depth: pin that the heal never bumps Version,
        // because TrainingActivity rows are keyed on
        // (PersonUserId, TrainingContentId, TrainingContentVersion) and
        // a stale version bump would orphan everything. The version on
        // the row before heal must equal the version on the row after
        // heal. (Also asserted by the other tests' Version assertions,
        // but this one names the invariant explicitly.)
        var root = NewTempDir();
        try
        {
            var path = Path.Combine(root, "guide.pdf");
            WritePdf(path, 2);

            var org = TestData.Org(Factory, "Org A");
            var content = TestData.PdfContent(Factory, org.Id, "Guide", totalPages: null);
            await using (var db = await Factory.CreateDbContextAsync())
            {
                var row = await db.TrainingContents.FindAsync(content.Id);
                row!.FilePathOrUrl = "/uploads/training/guide.pdf";
                await db.SaveChangesAsync();
            }

            var sut = new PdfPageCountHealer(Factory, new StubUploadPathProvider(root));
            await sut.TryHealAsync(content.Id);

            await using var db2 = await Factory.CreateDbContextAsync();
            var reloaded = await db2.TrainingContents.FindAsync(content.Id);
            Assert.Equal(content.Version, reloaded!.Version);
            Assert.Equal(content.VersionDateUtc, reloaded.VersionDateUtc);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TryHeal_EncryptedOrMalformedPdfBytes_ReturnsNull()
    {
        // Round-AI follow-on: the PdfPig catch inside TryHealAsync is
        // only reachable when the file exists on disk AND PdfPig throws.
        // Mirrors PdfPageCounterTests.Count_Throws_OnMalformedPdfBytes
        // which already established that PdfPig throws for non-parseable
        // bytes. The contract is: a throwing PdfPig is treated the same
        // as a missing file (return null, no row update). An encrypted
        // PDF would take the same path.
        var root = NewTempDir();
        try
        {
            var path = Path.Combine(root, "garbage.pdf");
            File.WriteAllBytes(path, "%PDF-1.7\nthis is not actually a parseable PDF"u8.ToArray());

            var org = TestData.Org(Factory, "Org A");
            var content = TestData.PdfContent(Factory, org.Id, "Guide", totalPages: null);
            await using (var db = await Factory.CreateDbContextAsync())
            {
                var row = await db.TrainingContents.FindAsync(content.Id);
                row!.FilePathOrUrl = "/uploads/training/garbage.pdf";
                await db.SaveChangesAsync();
            }

            var sut = new PdfPageCountHealer(Factory, new StubUploadPathProvider(root));
            var result = await sut.TryHealAsync(content.Id);

            Assert.Null(result);
            await using var db2 = await Factory.CreateDbContextAsync();
            var reloaded = await db2.TrainingContents.FindAsync(content.Id);
            Assert.Null(reloaded!.TotalPageCount);
            Assert.Equal(content.Version, reloaded.Version);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TryHeal_HttpUrlFilePathOrUrl_ReturnsNull()
    {
        // Format=PDF + http(s) URL is the EXTERNAL-PDF branch — there's
        // no on-disk file to re-extract from. The StartWith("http")
        // guard must skip without calling PdfPageCounter (which would
        // try to read a relative path as if it were a filesystem
        // location and throw).
        var root = NewTempDir();
        try
        {
            var org = TestData.Org(Factory, "Org A");
            var content = TestData.PdfContent(Factory, org.Id, "Guide", totalPages: null);
            await using (var db = await Factory.CreateDbContextAsync())
            {
                var row = await db.TrainingContents.FindAsync(content.Id);
                row!.FilePathOrUrl = "https://example.com/external-guide.pdf";
                await db.SaveChangesAsync();
            }

            var sut = new PdfPageCountHealer(Factory, new StubUploadPathProvider(root));
            var result = await sut.TryHealAsync(content.Id);

            Assert.Null(result);
            await using var db2 = await Factory.CreateDbContextAsync();
            var reloaded = await db2.TrainingContents.FindAsync(content.Id);
            Assert.Null(reloaded!.TotalPageCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TryHeal_NonPdfFormat_ReturnsNull()
    {
        // The "PDF page count unknown" gate only applies to
        // TrainingFormat.Pdf. Video/Slideshow rows have TotalPageCount
        // legitimately null, so the healer must skip them even if
        // someone uploads a non-PDF and the row has FilePathOrUrl set.
        var root = NewTempDir();
        try
        {
            var org = TestData.Org(Factory, "Org A");
            var content = TestData.TrainingContent(Factory, org.Id, "Safety Video");
            await using (var db = await Factory.CreateDbContextAsync())
            {
                var row = await db.TrainingContents.FindAsync(content.Id);
                row!.FilePathOrUrl = "/uploads/training/video.mp4";
                await db.SaveChangesAsync();
            }

            var sut = new PdfPageCountHealer(Factory, new StubUploadPathProvider(root));
            var result = await sut.TryHealAsync(content.Id);

            Assert.Null(result);
            await using var db2 = await Factory.CreateDbContextAsync();
            var reloaded = await db2.TrainingContents.FindAsync(content.Id);
            Assert.Null(reloaded!.TotalPageCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempDir()
    {
        // Create the directory here so call sites can write a file
        // into it BEFORE constructing the StubUploadPathProvider.
        // The stub's own constructor also calls Directory.CreateDirectory,
        // but that's a defensive no-op once this helper guarantees
        // the path is real on disk.
        var dir = Path.Combine(Path.GetTempPath(), "PdfHeal_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WritePdf(string path, int pageCount)
    {
        var builder = new PdfDocumentBuilder();
        for (var i = 0; i < pageCount; i++)
        {
            builder.AddPage(PageSize.A4);
        }
        File.WriteAllBytes(path, builder.Build());
    }

    /// <summary>
    /// Stub <see cref="IUploadPathProvider"/> that resolves file paths
    /// under a specific temp directory the test controls. Mirrors the
    /// real UploadPathProvider's contract (GetTrainingUploadPath strips
    /// directories via Path.GetFileName to refuse traversal) but
    /// without requiring a real IWebHostEnvironment.
    /// </summary>
    private sealed class StubUploadPathProvider : IUploadPathProvider
    {
        private readonly string _root;
        public StubUploadPathProvider(string root)
        {
            _root = root;
            Directory.CreateDirectory(root);
        }
        public string TrainingUploadsRoot => _root;
        public string GetTrainingUploadPath(string fileName) =>
            Path.Combine(_root, Path.GetFileName(fileName));
        public string GetSlotUploadsRoot(int slotId) =>
            Path.Combine(_root, "slots", slotId.ToString());
        public string GetSlotRelativePath(int slotId, string fileName) =>
            Path.Combine("slots", slotId.ToString(), Path.GetFileName(fileName)).Replace('\\', '/');
        public string GetSlotFileAbsolutePath(int slotId, string relativePath) =>
            throw new NotImplementedException("Test stub does not implement slot-path resolution.");
    }
}
