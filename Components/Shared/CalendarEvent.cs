namespace ServantSync.Components.Shared;

/// <summary>
/// A single event rendered on the <see cref="AssignmentCalendar"/> grid.
/// StatusColor maps to a Bootstrap bg-* class (primary, success, warning,
/// danger, secondary, info).
/// </summary>
public class CalendarEvent
{
    public int Id { get; set; }
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string? Location { get; set; }
    public string Status { get; set; } = "";
    public string StatusColor { get; set; } = "primary";
    public string Href { get; set; } = "";

    /// <summary>
    /// Optional arena id, populated for game events so the overlay
    /// calendar can filter by arena. Null for volunteer-shift events.
    /// </summary>
    public int? ArenaId { get; set; }

    /// <summary>Hover text: Title — HH:mm–HH:mm · Location. Safe to render as a Razor attribute value.</summary>
    public string Tooltip
    {
        get
        {
            var s = $"{Title} — {StartUtc:HH:mm}–{EndUtc:HH:mm}";
            if (!string.IsNullOrEmpty(Location)) s += " · " + Location;
            return s;
        }
    }
}
