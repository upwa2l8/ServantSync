namespace ServantSync.Services;

/// <summary>
/// Curated list of common IANA timezones for the org-admin TZ picker on
/// <c>Components/Pages/Organizations/Edit.razor</c>. Round-AV.
///
/// The picker is delimited by this list because the alternative — a free-text
/// input — produces two failure modes we don't want: (a) admins typing a
/// Windows-style id (e.g. <c>"Pacific Standard Time"</c>) that .NET can't
/// resolve without a tzdata package on Windows, and (b) admins hand-typing
/// typos that find their way into the database and silently break LocalTime
/// rendering until someone notices a shifted time. By constraining the input
/// to a curated list, both failure modes are eliminated: every option is
/// guaranteed to round-trip through <c>TimeZoneInfo.FindSystemTimeZoneById</c>
/// on the save path (tier 2's defensive <c>catch (Exception)</c> is still
/// loaded as a backstop, but it should never trigger from this list).
///
/// Curated list is intentionally broad (~30 ids) so an org in São Paulo or
/// Auckland finds their zone without rolling over to a "browser default"
/// fallback. Friendly display names follow the convention
/// <c>"Region — UTC offset (city)"</c> so a non-expert admin can scan the
/// picker without knowing the IANA id.
/// </summary>
public static class TimeZoneOptions
{
    /// <summary>One option in the curated list.</summary>
    public sealed record TimeZoneOption(string IanaId, string DisplayName, string Region);

    /// <summary>
    /// Curated baseline list. Ordered by region alphabetically within each
    /// group; regions ordered by population-coverage heuristic (Americas
    /// first because the deployed base is North-American). Adding a new id
    /// = appending one record to the right region — no DB migration
    /// needed because the field is already <c>string</c>.
    /// </summary>
    public static IReadOnlyList<TimeZoneOption> All { get; } = new List<TimeZoneOption>
    {
        // ---- Americas ----
        new("America/New_York",     "Eastern (New York)",       "Americas"),
        new("America/Chicago",      "Central (Chicago)",        "Americas"),
        new("America/Denver",       "Mountain (Denver)",        "Americas"),
        new("America/Phoenix",      "Mountain — no DST (Phoenix)", "Americas"),
        new("America/Los_Angeles",  "Pacific (Los Angeles)",    "Americas"),
        new("America/Anchorage",    "Alaska (Anchorage)",       "Americas"),
        new("Pacific/Honolulu",     "Hawaii (Honolulu)",        "Americas"),
        new("America/Toronto",      "Eastern (Toronto)",        "Americas"),
        new("America/Vancouver",    "Pacific (Vancouver)",      "Americas"),
        new("America/Mexico_City",  "Central (Mexico City)",    "Americas"),
        new("America/Sao_Paulo",    "Brasília (São Paulo)",     "Americas"),
        new("America/Buenos_Aires", "Argentina (Buenos Aires)", "Americas"),

        // ---- Europe ----
        new("Europe/London",        "GMT/BST (London)",         "Europe"),
        new("Europe/Lisbon",        "Western Europe (Lisbon)",  "Europe"),
        new("Europe/Paris",         "Central Europe (Paris)",   "Europe"),
        new("Europe/Berlin",        "Central Europe (Berlin)",  "Europe"),
        new("Europe/Madrid",        "Central Europe (Madrid)",  "Europe"),
        new("Europe/Rome",          "Central Europe (Rome)",    "Europe"),
        new("Europe/Amsterdam",     "Central Europe (Amsterdam)","Europe"),
        new("Europe/Athens",        "Eastern Europe (Athens)",  "Europe"),
        new("Europe/Moscow",        "Moscow (Moscow)",          "Europe"),

        // ---- Africa & Middle East ----
        new("Africa/Cairo",         "Egypt (Cairo)",            "Africa & Middle East"),
        new("Africa/Johannesburg",  "South Africa (Johannesburg)","Africa & Middle East"),
        new("Africa/Lagos",         "West Africa (Lagos)",      "Africa & Middle East"),
        new("Asia/Dubai",           "Gulf (Dubai)",             "Africa & Middle East"),
        new("Asia/Jerusalem",       "Israel (Jerusalem)",       "Africa & Middle East"),

        // ---- Asia & Pacific ----
        new("Asia/Kolkata",         "India (Kolkata)",          "Asia & Pacific"),
        new("Asia/Bangkok",         "Indochina (Bangkok)",      "Asia & Pacific"),
        new("Asia/Singapore",       "Singapore (Singapore)",    "Asia & Pacific"),
        new("Asia/Shanghai",        "China (Shanghai)",         "Asia & Pacific"),
        new("Asia/Tokyo",           "Japan (Tokyo)",            "Asia & Pacific"),
        new("Asia/Seoul",           "Korea (Seoul)",            "Asia & Pacific"),
        new("Australia/Perth",      "Western Australia (Perth)", "Asia & Pacific"),
        new("Australia/Sydney",     "Eastern Australia (Sydney)","Asia & Pacific"),
        new("Pacific/Auckland",     "New Zealand (Auckland)",   "Asia & Pacific"),

        // ---- UTC ----
        new("UTC",                  "UTC",                      "UTC"),
    };

    /// <summary>
    /// Group <see cref="All"/> by <see cref="TimeZoneOption.Region"/> in the
    /// order they appear in the source list (so the picker renders in the
    /// same order the curator wrote them, not an alphabetical re-sort).
    /// </summary>
    public static IEnumerable<IGrouping<string, TimeZoneOption>> ByRegion() =>
        All.GroupBy(o => o.Region);
}
