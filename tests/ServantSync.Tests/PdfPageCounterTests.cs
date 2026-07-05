using ServantSync.Services;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Unit tests for <see cref="ServantSync.Services.PdfPageCounter"/>.
///
/// <para>
/// These tests generate small synthetic PDFs via PdfPig's
/// <see cref="PdfDocumentBuilder"/>, write them to the system temp
/// directory, and assert that <c>PdfPageCounter.Count(path)</c> returns
/// the number of pages the builder produced. The tests cover both the
/// "no markup ever found" cases (empty path / missing file) and the
/// "real multi-page PDF" case that the regex-based implementation
/// silently broke — when a PDF's page dictionaries live inside a
/// compressed object stream the literal text "/Type /Page" was never
/// present in the raw bytes and the previous counter returned 1.
/// </para>
///
/// <para>
/// The synthetic PDFs written by PdfDocumentBuilder do NOT exercise the
/// compressed-object-stream path specifically — that's PdfPig's own
/// internal test surface. We rely on PdfPig's correctness for the
/// decompression/cross-reference resolution and only assert that Count
/// returns the same number its own <c>NumberOfPages</c> would.
/// </para>
/// </summary>
public class PdfPageCounterTests
{
    [Fact]
    public void Count_ReturnsZero_WhenPathIsNullOrEmpty()
    {
        Assert.Equal(0, PdfPageCounter.Count(""));
        Assert.Equal(0, PdfPageCounter.Count("   "));
    }

    [Fact]
    public void Count_ReturnsZero_WhenFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        // Path almost certainly doesn't exist; still guard so the test is
        // robust if a previous run failed to clean up.
        if (File.Exists(path)) File.Delete(path);
        Assert.Equal(0, PdfPageCounter.Count(path));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(7)]
    public void Count_ReturnsBuilderPageCount_OnMultiPagePdf(int pageCount)
    {
        var path = WritePdf(builder =>
        {
            for (var i = 0; i < pageCount; i++)
            {
                builder.AddPage(PageSize.A4);
            }
        });

        try
        {
            Assert.Equal(pageCount, PdfPageCounter.Count(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Count_Throws_OnMalformedPdfBytes()
    {
        // The contract is that the caller (Training/Edit.razor's
        // HandleUpload) wraps Count in a try/catch and treats any
        // exception as "page count unknown", which then surfaces as the
        // eligibility rule's "admin re-upload required" gate. We don't
        // pin the exact exception type — PdfPig has thrown several in
        // different versions — but we do pin "does not silently return
        // a wrong positive number" because that's the bug we're
        // replacing.
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, "%PDF-1.7\nthis is not actually a parseable PDF"u8.ToArray());

        try
        {
            Assert.ThrowsAny<Exception>(() => PdfPageCounter.Count(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Count_MatchesPdfPigsOwnNumberOfPages()
    {
        // Defence-in-depth: even if PdfPig changes its counting rules
        // in a future release, our wrapper must agree with the canonical
        // `NumberOfPages` for any well-formed PDF we feed it. This test
        // catches drift between our wrapper and the underlying parser.
        const int pages = 4;
        var path = WritePdf(builder =>
        {
            for (var i = 0; i < pages; i++)
            {
                builder.AddPage(PageSize.A4);
            }
        });

        try
        {
            using var doc = UglyToad.PdfPig.PdfDocument.Open(path);
            Assert.Equal(doc.NumberOfPages, PdfPageCounter.Count(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static string WritePdf(Action<PdfDocumentBuilder> configure)
    {
        var builder = new PdfDocumentBuilder();
        configure(builder);
        var bytes = builder.Build();
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
