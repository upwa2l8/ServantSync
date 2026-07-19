namespace ServantSync.Services.CalendarPdf;

/// <summary>
/// Data model for the calendar PDF generation request. Populated by the
/// minimal-API endpoint after resolving the slot, org, occurrences, and
/// assignments from the database.
/// </summary>
public class CalendarPdfRequest
{
    public string OrganizationName { get; set; } = "";
    public string? OrganizationDescription { get; set; }
    public string SlotName { get; set; } = "";
    public string? SlotLocation { get; set; }
    public string? TimeZoneDisplayName { get; set; }
    public string Scope { get; set; } = "month"; // month | week | day
    public DateTime StartDate { get; set; }
    public DateTime GeneratedUtc { get; set; }
    public string? OrgJoinUrl { get; set; }
    public string? BaseUri { get; set; }
    public string? OpenPageUrl { get; set; }
    public bool ShowVolunteerNames { get; set; }
    public List<CalendarOccurrence> Occurrences { get; set; } = new();
}

/// <summary>
/// A single occurrence for the calendar PDF. Mirrors the SlotOccurrence
/// + Assignment data resolved by the endpoint.
/// </summary>
public class CalendarOccurrence
{
    public int Id { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string? AssignedVolunteerName { get; set; }
    public bool IsOpen => string.IsNullOrEmpty(AssignedVolunteerName);
}

/// <summary>
/// Calendar PDF scope options.
/// </summary>
public static class CalendarScope
{
    public const string Month = "month";
    public const string Week = "week";
    public const string Day = "day";

    /// <summary>
    /// Returns the date range for the given scope starting from the specified date.
    /// </summary>
    public static (DateTime FromUtc, DateTime ToUtc) GetRange(string scope, DateTime startDate, TimeZoneInfo tz)
    {
        // Convert start date (local) to UTC for the query range.
        var startLocal = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, tz);

        return scope switch
        {
            Week => (startUtc, startUtc.AddDays(7)),
            Day => (startUtc, startUtc.AddDays(1)),
            _ => (startUtc, startUtc.AddMonths(1)), // month
        };
    }
}
