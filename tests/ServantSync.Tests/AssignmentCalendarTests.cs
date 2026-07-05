using Bunit;
using Microsoft.Extensions.DependencyInjection;
using ServantSync.Components.Shared;
using ServantSync.Services;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Component tests for <see cref="AssignmentCalendar"/>. Renders the
/// calendar in isolation with a hand-built event list and asserts on
/// the month grid, the "Today" button, the "+N more" affordance, and the
/// Month/Week toggle.
/// </summary>
public class AssignmentCalendarTests : TestContext
{
    public AssignmentCalendarTests()
    {
        // AssignmentCalendar renders <LocalTime> pills internally; LocalTime
        // @injects the scoped UserTimeZoneProvider. Mirror Program.cs's
        // AddScoped<UserTimeZoneProvider>() so the dependency resolves.
        Services.AddScoped<UserTimeZoneProvider>();
    }

    private static CalendarEvent Ev(string title, DateTime start, DateTime end, string statusColor = "primary") =>
        new() { Title = title, StartUtc = start, EndUtc = end, StatusColor = statusColor, Href = "#" };

    [Fact]
    public void MonthView_RendersSevenDayHeaders()
    {
        var cut = RenderComponent<AssignmentCalendar>(parameters => parameters
            .Add(p => p.Events, new List<CalendarEvent>()));

        foreach (var dn in new[] { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" })
        {
            Assert.Contains(dn, cut.Markup);
        }
    }

    [Fact]
    public void ShowsEventCountSummary()
    {
        var events = new List<CalendarEvent>
        {
            Ev("A", new DateTime(2026, 7, 9, 14, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 9, 15, 0, 0, DateTimeKind.Utc)),
            Ev("B", new DateTime(2026, 7, 10, 14, 0, 0, DateTimeKind.Utc), new DateTime(2026, 7, 10, 15, 0, 0, DateTimeKind.Utc)),
        };
        var cut = RenderComponent<AssignmentCalendar>(parameters => parameters
            .Add(p => p.Events, events)
            .Add(p => p.AnchorDate, new DateTime(2026, 7, 1)));

        Assert.Contains("2 events in view", cut.Markup);
    }

    [Fact]
    public void PlusNMore_Affordance_Appears_WhenMoreThanThreeEventsInADay()
    {
        var day = new DateTime(2026, 7, 9);
        var events = Enumerable.Range(0, 5)
            .Select(i => Ev($"E{i}",
                day.AddHours(8 + i),
                day.AddHours(9 + i)))
            .ToList();
        var cut = RenderComponent<AssignmentCalendar>(parameters => parameters
            .Add(p => p.Events, events)
            .Add(p => p.AnchorDate, new DateTime(2026, 7, 1)));

        // 5 events − 3 visible = 2 more.
        Assert.Contains("+2 more", cut.Markup);
    }

    [Fact]
    public void ToggleToWeekView_RendersHourHeaders()
    {
        var cut = RenderComponent<AssignmentCalendar>(parameters => parameters
            .Add(p => p.Events, new List<CalendarEvent>())
            .Add(p => p.ViewMode, AssignmentCalendar.CalendarViewMode.Week));

        // 6 AM through 9 PM (10 PM exclusive) → 16 hour labels.
        Assert.Contains("6 AM", cut.Markup);
        Assert.Contains("9 PM", cut.Markup);
    }

    [Fact]
    public void MonthAndWeekToggle_ButtonsExist()
    {
        var cut = RenderComponent<AssignmentCalendar>(parameters => parameters
            .Add(p => p.Events, new List<CalendarEvent>()));

        // The Month and Week buttons are always present in the header.
        Assert.Contains(">Month<", cut.Markup);
        Assert.Contains(">Week<", cut.Markup);
    }

    [Fact]
    public void ClickingNext_AdvancesTheAnchor()
    {
        DateTime? newAnchor = null;
        var cut = RenderComponent<AssignmentCalendar>(parameters => parameters
            .Add(p => p.Events, new List<CalendarEvent>())
            .Add(p => p.AnchorDate, new DateTime(2026, 7, 1))
            .Add(p => p.AnchorDateChanged, v => newAnchor = v));

        // The header has a "›" (Next) button.
        cut.FindAll("button").Single(b => b.GetAttribute("aria-label") == "Next").Click();
        Assert.NotNull(newAnchor);
        Assert.Equal(new DateTime(2026, 8, 1), newAnchor!.Value);
    }
}
