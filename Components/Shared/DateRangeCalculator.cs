namespace ServantSync.Components.Shared;

/// <summary>
/// Pure math for resolving a <see cref="DateRangeChips.RangeMode"/> into a
/// concrete (fromUtc, toUtc) window. Extracted so it can be unit-tested
/// without instantiating the Blazor component.
///
/// All windows are inclusive-start / exclusive-end. <c>Custom</c> is
/// deterministic — it does not depend on <paramref name="nowUtc"/>.
/// <c>All</c> returns the .NET min/max so the caller can use it directly
/// in <c>WHERE</c> clauses without special-casing.
/// </summary>
public static class DateRangeCalculator
{
    public static (DateTime FromUtc, DateTime ToUtc) Resolve(
        DateRangeChips.RangeMode mode,
        DateTime customFrom,
        DateTime customTo,
        DateTime nowUtc)
    {
        var today = nowUtc.Date;
        return mode switch
        {
            DateRangeChips.RangeMode.Today  => (today, today.AddDays(1)),
            DateRangeChips.RangeMode.Next7  => (today, today.AddDays(7)),
            DateRangeChips.RangeMode.Next14 => (today, today.AddDays(14)),
            DateRangeChips.RangeMode.Next30 => (today, today.AddDays(30)),
            DateRangeChips.RangeMode.Next90 => (today, today.AddDays(90)),
            DateRangeChips.RangeMode.All    => (DateTime.MinValue, DateTime.MaxValue),
            DateRangeChips.RangeMode.Custom => (customFrom.Date, customTo.Date.AddDays(1)),
            _                                => (today, today.AddDays(14)),
        };
    }
}
