namespace ServantSync.Models;

/// <summary>
/// Curated catalogue of MudBlazor Material icons that make sense for
/// ministry / volunteer-opportunity labelling. Each entry pairs the
/// fully-qualified icon constant string (e.g. "Icons.Material.Outlined.Church")
/// with a human-friendly label shown in the icon-picker dropdown.
/// </summary>
public static class MinistryIcons
{
    /// <summary>
    /// Fallback icon when a ministry has no icon set.
    /// </summary>
    public const string Default = "Icons.Material.Outlined.Church";

    public record IconOption(string Value, string Label);

    public static readonly IReadOnlyList<IconOption> All = new IconOption[]
    {
        new("Icons.Material.Outlined.Church",              "Church"),
        new("Icons.Material.Outlined.MenuBook",            "Bible / Study"),
        new("Icons.Material.Outlined.MusicNote",           "Worship / Music"),
        new("Icons.Material.Outlined.ChildCare",           "Children"),
        new("Icons.Material.Outlined.School",              "Education"),
        new("Icons.Material.Outlined.Park",                "Outdoor / Nature"),
        new("Icons.Material.Outlined.Restaurant",          "Food / Dining"),
        new("Icons.Material.Outlined.Campaign",            "Outreach"),
        new("Icons.Material.Outlined.Groups",              "Small Groups"),
        new("Icons.Material.Outlined.VolunteerActivism",   "Charity"),
        new("Icons.Material.Outlined.HealthAndSafety",     "Health / Safety"),
        new("Icons.Material.Outlined.Build",               "Building / Maintenance"),
        new("Icons.Material.Outlined.Palette",             "Arts / Crafts"),
        new("Icons.Material.Outlined.SportsSoccer",        "Sports / Soccer"),
        new("Icons.Material.Outlined.SportsBaseball",      "Sports / Baseball"),
        new("Icons.Material.Outlined.SportsBasketball",    "Sports / Basketball"),
        new("Icons.Material.Outlined.Celebration",         "Events / Celebrations"),
        new("Icons.Material.Outlined.Elderly",             "Senior Care"),
        new("Icons.Material.Outlined.FamilyRestroom",      "Family"),
        new("Icons.Material.Outlined.Hiking",              "Hiking / Retreats"),
        new("Icons.Material.Outlined.AutoAwesome",         "Special / Misc"),
    };

}
