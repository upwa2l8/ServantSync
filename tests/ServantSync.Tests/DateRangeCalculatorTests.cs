using ServantSync.Components.Shared;
using Xunit;

namespace ServantSync.Tests;

public class DateRangeCalculatorTests
{
    private static readonly DateTime NowUtc   = new(2026, 7, 9, 14, 30, 45, DateTimeKind.Utc);
    private static readonly DateTime TodayUtc = new(2026, 7, 9, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Today_ReturnsTodayToTomorrow()
    {
        var (from, to) = DateRangeCalculator.Resolve(DateRangeChips.RangeMode.Today, default, default, NowUtc);
        Assert.Equal(TodayUtc, from);
        Assert.Equal(TodayUtc.AddDays(1), to);
    }

    [Theory]
    [InlineData(DateRangeChips.RangeMode.Next7,  7)]
    [InlineData(DateRangeChips.RangeMode.Next14, 14)]
    [InlineData(DateRangeChips.RangeMode.Next30, 30)]
    [InlineData(DateRangeChips.RangeMode.Next90, 90)]
    public void Next_Presets_StartAtTodayAndEndAfterGivenDays(DateRangeChips.RangeMode mode, int days)
    {
        var (from, to) = DateRangeCalculator.Resolve(mode, default, default, NowUtc);
        Assert.Equal(TodayUtc, from);
        Assert.Equal(TodayUtc.AddDays(days), to);
    }

    [Fact]
    public void Next_Presets_UseDateComponentOfNowNotTimeOfDay()
    {
        // A non-midnight `now` should still produce a midnight-anchored window
        // so the SQL `>= from AND < to` comparison is well-defined.
        var (from, to) = DateRangeCalculator.Resolve(DateRangeChips.RangeMode.Next7, default, default, NowUtc);
        Assert.Equal(TimeSpan.Zero, from.TimeOfDay);
        Assert.Equal(TimeSpan.Zero, to.TimeOfDay);
    }

    [Fact]
    public void All_ReturnsMinAndMax()
    {
        var (from, to) = DateRangeCalculator.Resolve(DateRangeChips.RangeMode.All, default, default, NowUtc);
        Assert.Equal(DateTime.MinValue, from);
        Assert.Equal(DateTime.MaxValue, to);
    }

    [Fact]
    public void Custom_EndIsExclusive_AddsOneDayToCustomTo()
    {
        var customFrom = new DateTime(2026, 7, 1);
        var customTo   = new DateTime(2026, 7, 14);
        var (from, to) = DateRangeCalculator.Resolve(DateRangeChips.RangeMode.Custom, customFrom, customTo, NowUtc);
        Assert.Equal(customFrom, from);
        Assert.Equal(customTo.AddDays(1), to);
    }

    [Fact]
    public void Custom_IsDeterministicRegardlessOfNow()
    {
        var (from1, to1) = DateRangeCalculator.Resolve(
            DateRangeChips.RangeMode.Custom,
            new DateTime(2025, 1, 1), new DateTime(2025, 1, 7),
            NowUtc);
        var (from2, to2) = DateRangeCalculator.Resolve(
            DateRangeChips.RangeMode.Custom,
            new DateTime(2025, 1, 1), new DateTime(2025, 1, 7),
            new DateTime(2030, 6, 1, 3, 4, 5, DateTimeKind.Utc));
        Assert.Equal(from1, from2);
        Assert.Equal(to1, to2);
    }

    [Fact]
    public void Custom_NormalizesTimeOfDayToMidnight()
    {
        // The page's date inputs round to whole days; the calculator must
        // also drop the time component so chip state stays consistent.
        var (from, to) = DateRangeCalculator.Resolve(
            DateRangeChips.RangeMode.Custom,
            new DateTime(2026, 7, 1, 13, 0, 0),
            new DateTime(2026, 7, 14, 22, 30, 0),
            NowUtc);
        Assert.Equal(TimeSpan.Zero, from.TimeOfDay);
        Assert.Equal(TimeSpan.Zero, to.TimeOfDay);
    }
}
