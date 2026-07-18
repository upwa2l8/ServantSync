namespace ServantSync.Components.Shared;

/// <summary>
/// Static helper that resolves a timezone for PDF generation using the same
/// 3-tier chain that <c>LocalTime.razor</c> uses for on-screen rendering.
/// Extracted here so the PDF builder (which has no Blazor circuit) can
/// resolve the timezone identically.
///
/// Resolution precedence:
///   1. Requested timezone (user-picked from the zone-picker dropdown).
///   2. Organization.TimeZoneId (the org-level IANA id from the database).
///   3. Server local (last resort — server's own timezone).
/// </summary>
public static class TimeZoneResolver
{
    /// <summary>
    /// Resolves a <see cref="TimeZoneInfo"/> for the PDF using the 3-tier chain.
    /// </summary>
    /// <param name="requestedTz">User-selected IANA id from the zone-picker dropdown (may be null).</param>
    /// <param name="orgTzId">Organization.TimeZoneId from the database (may be null).</param>
    /// <returns>A valid <see cref="TimeZoneInfo"/> — never null.</returns>
    public static TimeZoneInfo Resolve(string? requestedTz, string? orgTzId)
    {
        // Tier 1: user-selected from the zone-picker dropdown.
        if (!string.IsNullOrEmpty(requestedTz))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(requestedTz); }
            catch { /* Fall through to tier 2 */ }
        }

        // Tier 2: org-level IANA id from the database.
        if (!string.IsNullOrEmpty(orgTzId))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(orgTzId); }
            catch { /* Fall through to tier 3 */ }
        }

        // Tier 3: server local (last resort).
        return TimeZoneInfo.Local;
    }

    /// <summary>
    /// Converts a UTC instant to the resolved timezone and formats it.
    /// </summary>
    public static DateTime ToLocal(DateTime utc, TimeZoneInfo tz)
    {
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
    }
}
