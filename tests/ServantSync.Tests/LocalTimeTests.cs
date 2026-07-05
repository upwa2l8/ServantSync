using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ServantSync.Components.Shared;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Component tests for <see cref="LocalTime"/>. Covers:
///   - Round-AV 3-tier resolution precedence (provider &gt; fallback &gt; server local).
///   - Tier-2 defensive catch on invalid <c>FallbackTimeZoneId</c>.
///   - Step 1 bullet regression — guards against the literal six-character
///     <c>\u2022</c> rendering as the text "u2022" instead of the bullet "•".
/// </summary>
public class LocalTimeTests : TestContext
{
    public LocalTimeTests()
    {
        // Mirror Program.cs's AddScoped<UserTimeZoneProvider>() so the DI
        // graph resolves; without this the [Inject] property is null and
        // every test would inadvertently exercise the tier-1-skip path.
        Services.AddScoped<UserTimeZoneProvider>();
    }

    /// <summary>
    /// A fixed UTC instant chosen to make the bug visible in any zone.
    /// 2026-07-04 14:00 UTC:
    ///   - America/New_York (EDT, UTC-4) → 10:00 AM
    ///   - America/Los_Angeles (PDT, UTC-7) → 7:00 AM
    ///   - Europe/London (BST, UTC+1) → 3:00 PM
    /// </summary>
    private static readonly DateTime FixedUtc = new(2026, 7, 4, 14, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Pulls the user-facing text out of the rendered <c>&lt;time&gt;</c>
    /// element. Uses <see cref="IRenderedComponent{T}.Find(string)"/> so
    /// the assertion isn't coupled to the surrounding whitespace /
    /// attribute layout that <see cref="IRenderedComponent{T}.Markup"/>
    /// returns; future markup changes (child nodes inside the element)
    /// won't silently break the helper.
    /// </summary>
    private static string ExtractTime(IRenderedComponent<LocalTime> cut) =>
        cut.Find("time").TextContent.Trim();

    [Fact]
    public void ProviderTimeZone_TakesPrecedence_OverFallback()
    {
        // Tier 1 (provider) wins — even though tier 2 would have rendered
        // 10:00 AM NY, the configured provider zone (LA) renders 07:00 AM.
        var provider = Services.GetRequiredService<UserTimeZoneProvider>();
        provider.TimeZoneId = "America/Los_Angeles";

        var cut = RenderComponent<LocalTime>(parameters => parameters
            .Add(p => p.Utc, FixedUtc)
            .Add(p => p.Format, "HH:mm")
            .Add(p => p.FallbackTimeZoneId, "America/New_York"));

        Assert.Equal("07:00", ExtractTime(cut));
    }

    [Fact]
    public void FallbackTimeZoneId_Used_WhenProviderMissing()
    {
        // Tier 1 skipped (provider has no id) → tier 2 (org-TZ) supplies
        // the resolution path; should render 10:00 AM NY.
        var provider = Services.GetRequiredService<UserTimeZoneProvider>();
        provider.TimeZoneId = null;

        var cut = RenderComponent<LocalTime>(parameters => parameters
            .Add(p => p.Utc, FixedUtc)
            .Add(p => p.Format, "HH:mm")
            .Add(p => p.FallbackTimeZoneId, "America/New_York"));

        Assert.Equal("10:00", ExtractTime(cut));
    }

    [Fact]
    public void FallbackTimeZoneId_Used_WhenProviderEmpty()
    {
        // Empty string is treated identically to null for the tier-1 gate.
        var provider = Services.GetRequiredService<UserTimeZoneProvider>();
        provider.TimeZoneId = "";

        var cut = RenderComponent<LocalTime>(parameters => parameters
            .Add(p => p.Utc, FixedUtc)
            .Add(p => p.Format, "HH:mm")
            .Add(p => p.FallbackTimeZoneId, "America/New_York"));

        Assert.Equal("10:00", ExtractTime(cut));
    }

    [Fact]
    public void InvalidFallbackId_FallsThrough_SilentlyWithoutThrowing()
    {
        // Tier-2 defensive catch: an admin typo in the database must not
        // crash a page render. Tier 2's `catch` swallows the
        // FindSystemTimeZoneById failure, then tier 3 (server local)
        // takes over. The render path MUST NOT throw, and the rendered
        // output MUST match the hh:mm format that tier 3 produces — not
        // be blank, not be a literal "u2022", not be a stack trace.
        var provider = Services.GetRequiredService<UserTimeZoneProvider>();
        provider.TimeZoneId = null;

        var cut = RenderComponent<LocalTime>(parameters => parameters
            .Add(p => p.Utc, FixedUtc)
            .Add(p => p.Format, "HH:mm")
            .Add(p => p.FallbackTimeZoneId, "NotAReal/IanaZone"));

        var rendered = ExtractTime(cut);
        Assert.Matches(@"^\d{2}:\d{2}$", rendered);
    }

    [Fact]
    public void ProviderTimeZone_IsStrict_ThrowsOnInvalidId()
    {
        // The 3-tier design deliberately contrasts tier 1 (strict; throws
        // on a bad id from the JS layer so we surface a real bug) against
        // tier 2 (defensive; swallows a bad org-TZ so an admin's typo
        // can't break a page). This test pins the asymmetry between
        // tier 1 and tier 2 by asserting explicit throw on tier 1,
        // rather than relying on the absence of a pass-through test.
        //
        // Wrapped in a manual try/catch (rather than Assert.ThrowsAny) so
        // the test fails loudly with a clear message if bUnit's exception
        // propagation policy ever buffers the throw instead of surfacing
        // it synchronously from RenderComponent.
        var provider = Services.GetRequiredService<UserTimeZoneProvider>();
        provider.TimeZoneId = "Definitely.NotAReal/IanaZone";

        try
        {
            RenderComponent<LocalTime>(parameters => parameters
                .Add(p => p.Utc, FixedUtc)
                .Add(p => p.Format, "HH:mm")
                .Add(p => p.FallbackTimeZoneId, null));
            Assert.Fail(
                "Expected TimeZoneNotFoundException from tier 1 (strict); " +
                "the render path returned without throwing.");
        }
        catch (TimeZoneNotFoundException)
        {
            // documented behavior: tier 1 is strict.
        }
        catch (Exception ex)
        {
            Assert.Fail(
                $"Expected TimeZoneNotFoundException from tier 1; got " +
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    [Fact]
    public void OpenRazor_BulletFormat_IsWrappedInAtParen_SoRazorResolvesEscape()
    {
        // Round-AV Step 1 root-cause guard. The /Open page's time stamp
        // (Components/Pages/Open.razor) intentionally uses a bullet U+2022
        // between the date and the time. Razor attribute strings are
        // HTML values, NOT C# string literals, so `\u2022` written
        // literally inside `Format="..."` would pass the six-character
        // text "u2022" to DateTime.ToString and render the literal word
        // "u2022" on the page. The fix wraps the value in `@(...)` so
        // Razor parses it as a C# string literal whose escape sequence
        // resolves to the actual bullet character.
        //
        // This test fails loudly if a future contributor drops the
        // `@(...)` wrap — which is the regression we want to guard
        // against. The path walk handles both the in-place test run and
        // a `dotnet test` from the repo root.
        var path = LocateOpenRazor();
        Assert.True(File.Exists(path),
            $"Could not find Open.razor at resolved path '{path}'. " +
            $"BaseDirectory was '{AppContext.BaseDirectory}'.");

        var source = File.ReadAllText(path);

        // The literal-string form (BUGGY):
        //   Format="ddd, MMM d \u2022 h:mm tt"
        // The wrapped-C#-literal form (CORRECT):
        //   Format="@("ddd, MMM d \u2022 h:mm tt")"
        const string buggyForm = "Format=\"ddd, MMM d \\u2022 h:mm tt\"";
        const string fixedForm = "Format=\"@(\"ddd, MMM d \\u2022 h:mm tt\")\"";

        Assert.DoesNotContain(buggyForm, source);
        Assert.Contains(fixedForm, source);
    }

    /// <summary>
    /// Walk up from <see cref="AppContext.BaseDirectory"/> until we locate
    /// <c>Components/Pages/Open.razor</c>, returning the directory that
    /// contains it. The walk is bounded so a deleted / moved project can't
    /// spin forever.
    /// </summary>
    private static string LocateOpenRazor()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            var candidate = Path.Combine(dir, "Components", "Pages", "Open.razor");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir) ?? string.Empty;
        }
        return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Components", "Pages", "Open.razor");
    }
}
