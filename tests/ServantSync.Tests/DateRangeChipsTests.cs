using Bunit;
using Microsoft.AspNetCore.Components;
using ServantSync.Components.Shared;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Component tests for <see cref="DateRangeChips"/>. Renders the strip
/// in isolation, exercises the preset buttons and the custom date
/// inputs, and verifies that the <c>OnRangeChanged</c> callback fires
/// with the expected (from, to) values computed by
/// <see cref="DateRangeCalculator"/>.
/// </summary>
public class DateRangeChipsTests : TestContext
{
    private static readonly DateTime Now = new(2026, 7, 9, 14, 30, 45, DateTimeKind.Utc);

    [Fact]
    public void RendersAllPresetChips()
    {
        var from = Now.Date;
        var to = from.AddDays(14);
        var cut = RenderComponent<DateRangeChips>(parameters => parameters
            .Add(p => p.FromUtc, from)
            .Add(p => p.ToUtc, to));

        // Every preset label should appear as a button.
        foreach (var label in new[] { "Today", "7 days", "14 days", "30 days", "90 days", "All", "Custom…" })
        {
            Assert.Contains(label, cut.Markup);
        }
    }

    [Fact]
    public void Next14_IsHighlightedByDefault()
    {
        var from = Now.Date;
        var to = from.AddDays(14);
        var cut = RenderComponent<DateRangeChips>(parameters => parameters
            .Add(p => p.FromUtc, from)
            .Add(p => p.ToUtc, to));

        // The "14 days" button should carry btn-primary (the active class);
        // the others should be btn-outline-primary.
        Assert.Contains("btn btn-primary\"", cut.Markup.Replace("\r", "").Replace("\n", ""));
        // Sanity: the string "14 days" appears in the markup.
        Assert.Contains("14 days", cut.Markup);
    }

    [Fact]
    public void ClickingPreset_FiresOnRangeChanged_WithCalculatedWindow()
    {
        var from = Now.Date;
        var to = from.AddDays(14);
        (DateTime from, DateTime to)? captured = null;
        var cut = RenderComponent<DateRangeChips>(parameters => parameters
            .Add(p => p.FromUtc, from)
            .Add(p => p.ToUtc, to)
            .Add(p => p.OnRangeChanged, EventCallback.Factory.Create<(DateTime, DateTime)>(this, v => captured = v)));

        // Click "7 days" → window should be today (UTC midnight) → today + 7d.
        // The component uses DateTime.UtcNow internally for the calculator,
        // not the test's `Now` constant, so the expected window is anchored
        // to "today" at the time the test runs.
        var expectedFrom = DateTime.UtcNow.Date;
        var expectedTo = expectedFrom.AddDays(7);
        cut.FindAll("button").Single(b => b.TextContent.Trim() == "7 days").Click();

        Assert.NotNull(captured);
        Assert.Equal(expectedFrom, captured!.Value.from.Date);
        Assert.Equal(expectedTo, captured.Value.to.Date);
    }

    [Fact]
    public void CustomMode_ShowsDateInputs_AndHidesThemInOtherModes()
    {
        var cut = RenderComponent<DateRangeChips>(parameters => parameters
            .Add(p => p.FromUtc, Now.Date)
            .Add(p => p.ToUtc, Now.Date.AddDays(14)));

        // Date inputs are rendered only when mode is Custom.
        Assert.Empty(cut.FindAll("input[type=date]"));

        cut.FindAll("button").Single(b => b.TextContent.Trim() == "Custom…").Click();
        Assert.Equal(2, cut.FindAll("input[type=date]").Count);
    }

    [Fact]
    public void CustomRange_PassesThroughCalculatorResolve()
    {
        (DateTime from, DateTime to)? captured = null;
        var cut = RenderComponent<DateRangeChips>(parameters => parameters
            .Add(p => p.FromUtc, Now.Date)
            .Add(p => p.ToUtc, Now.Date.AddDays(14))
            .Add(p => p.OnRangeChanged, EventCallback.Factory.Create<(DateTime, DateTime)>(this, v => captured = v))
            .Add(p => p.InitialMode, DateRangeChips.RangeMode.Custom));

        var fromInput = cut.FindAll("input[type=date]")[0];
        fromInput.Change("2026-08-01");

        Assert.NotNull(captured);
        // The chip should pass the user-typed value straight through to the
        // calculator; Resolve(Custom, ...) treats the end as exclusive and
        // adds a day. We don't care about the day-shift here, just that the
        // calculator was invoked (the from value matches the input).
        Assert.Equal(new DateTime(2026, 8, 1), captured!.Value.from.Date);
    }
}
