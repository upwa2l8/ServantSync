using ServantSync.Components.Shared;
using Xunit;

namespace ServantSync.Tests;

public class CalendarEventTooltipTests
{
    private static readonly DateTime Start = new(2026, 7, 9, 14, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End   = new(2026, 7, 9, 15, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Tooltip_WithoutLocation_FormatsTitleAndTimeOnly()
    {
        var ev = new CalendarEvent { Title = "Bob \u00b7 Welcome", StartUtc = Start, EndUtc = End, Location = null };
        Assert.Equal("Bob \u00b7 Welcome \u2014 14:00\u201315:30", ev.Tooltip);
    }

    [Fact]
    public void Tooltip_WithEmptyLocation_FormatsTitleAndTimeOnly()
    {
        var ev = new CalendarEvent { Title = "Bob \u00b7 Welcome", StartUtc = Start, EndUtc = End, Location = "" };
        Assert.Equal("Bob \u00b7 Welcome \u2014 14:00\u201315:30", ev.Tooltip);
    }

    [Fact]
    public void Tooltip_WithLocation_AppendsLocationWithSeparator()
    {
        var ev = new CalendarEvent { Title = "Bob \u00b7 Welcome", StartUtc = Start, EndUtc = End, Location = "Lobby" };
        Assert.Equal("Bob \u00b7 Welcome \u2014 14:00\u201315:30 \u00b7 Lobby", ev.Tooltip);
    }

    [Fact]
    public void Tooltip_FormatsTimesWithLeadingZeros()
    {
        var ev = new CalendarEvent
        {
            Title = "Early",
            StartUtc = new DateTime(2026, 7, 9, 6, 5, 0, DateTimeKind.Utc),
            EndUtc   = new DateTime(2026, 7, 9, 7, 0, 0, DateTimeKind.Utc),
            Location = null,
        };
        Assert.Equal("Early \u2014 06:05\u201307:00", ev.Tooltip);
    }
}
