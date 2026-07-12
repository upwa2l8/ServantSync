namespace ServantSync.Components.Shared;

/// <summary>
/// Shared static helpers for building Razor-page hrefs and mailto links.
///
/// Replaces the inline <c>@($\"…\")</c> Razor expressions that the .NET 9
/// Razor parser chokes on when too many appear in a single file (the
/// CS1002 at the generated C# line that broke ServiceSlots/Detail.razor
/// at 11+ such patterns). Centralizing the URL shape here also means
/// renames / reorgs of a route only touch one place.
///
/// All path helpers return PATH-RELATIVE URLs (no leading <c>/</c>) so
/// Blazor's router resolves them against the current page. The single
/// exception is <see cref="DocDownloadHref"/> which is absolute because
/// it hits a minimal-API endpoint outside the Blazor page tree.
/// </summary>
public static class LinkBuilder
{
    /// <summary>Profile / detail page for a person. <c>People/{userId}</c>.</summary>
    public static string PersonHref(string userId) => $"People/{userId}";

    /// <summary>
    /// <c>mailto:{email}</c>. Returns empty string for null/empty so a
    /// non-nullable <c>string</c> signature doesn't lie about its
    /// contract — callers that want to surface a disabled link on null
    /// can check for empty and the existing <c>IsNullOrWhiteSpace</c>
    /// gate they already have covers the rendering decision.
    /// </summary>
    public static string MailtoHref(string? email) =>
        string.IsNullOrEmpty(email) ? string.Empty : $"mailto:{email}";

    /// <summary>Organization detail page. <c>Organizations/{orgId}</c>.</summary>
    public static string OrganizationHref(int orgId) => $"Organizations/{orgId}";

    /// <summary>Ministry detail page. <c>Organizations/{orgId}/Ministries/{ministryId}</c>.</summary>
    public static string MinistryHref(int orgId, int ministryId) =>
        $"Organizations/{orgId}/Ministries/{ministryId}";

    /// <summary>League detail page. <c>Leagues/{ministryId}</c>.</summary>
    public static string LeagueHref(int ministryId) => $"Leagues/{ministryId}";

    /// <summary>Service-slot detail page. <c>Organizations/{orgId}/Ministries/{ministryId}/Roles/{slotId}</c>.</summary>
    public static string SlotHref(int orgId, int ministryId, int slotId) =>
        $"Organizations/{orgId}/Ministries/{ministryId}/Roles/{slotId}";

    /// <summary>Service-slot "schedule one" page. <c>…/Roles/{slotId}/Schedule</c>.</summary>
    public static string SlotScheduleHref(int orgId, int ministryId, int slotId) =>
        $"Organizations/{orgId}/Ministries/{ministryId}/Roles/{slotId}/Schedule";

    /// <summary>Service-slot "edit" page. <c>…/Roles/{slotId}/Edit</c>.</summary>
    public static string SlotEditHref(int orgId, int ministryId, int slotId) =>
        $"Organizations/{orgId}/Ministries/{ministryId}/Roles/{slotId}/Edit";

    /// <summary>Service-slot "schedule series" page. <c>…/Roles/{slotId}/Series</c>.</summary>
    public static string SlotSeriesHref(int orgId, int ministryId, int slotId) =>
        $"Organizations/{orgId}/Ministries/{ministryId}/Roles/{slotId}/Series";

    /// <summary>Organization "edit" page. <c>Organizations/{orgId}/Edit</c>.</summary>
    public static string OrganizationEditHref(int orgId) =>
        $"Organizations/{orgId}/Edit";

    /// <summary>Training-content detail / take page. <c>Training/{contentId}</c>.</summary>
    public static string TrainingHref(int trainingContentId) =>
        $"Training/{trainingContentId}";

    /// <summary>
    /// Training-session detail page. <c>Organizations/{orgId}/Training/Sessions/{sessionId}</c>.
    /// Sessions live under their org (unlike <see cref="TrainingHref"/>
    /// which is global), so the URL carries the org id.
    /// </summary>
    public static string TrainingSessionHref(int orgId, int sessionId) =>
        $"Organizations/{orgId}/Training/Sessions/{sessionId}";

    /// <summary>
    /// Document-download endpoint. PATH-ABSOLUTE because it hits a
    /// minimal-API route at <c>/slots/{slotId}/documents/{docId}/download</c>
    /// registered in <c>Program.cs</c>, not a Razor page. Stays here for
    /// one-stop shopping even though it's the odd one out.
    /// </summary>
    public static string DocDownloadHref(int serviceSlotId, int documentId) =>
        $"/slots/{serviceSlotId}/documents/{documentId}/download";

    // ── Ministry action helpers ──────────────────────────────────────────

    /// <summary>Ministry "edit" page. <c>Organizations/{orgId}/Ministries/{ministryId}/Edit</c>.</summary>
    public static string MinistryEditHref(int orgId, int ministryId) =>
        $"Organizations/{orgId}/Ministries/{ministryId}/Edit";

    /// <summary>Ministry "view signups" page. <c>Organizations/{orgId}/Ministries/{ministryId}/Signups</c>.</summary>
    public static string MinistrySignupsHref(int orgId, int ministryId) =>
        $"Organizations/{orgId}/Ministries/{ministryId}/Signups";

    /// <summary>Ministry "new" page (under an org). <c>Organizations/{orgId}/Ministries/New</c>.</summary>
    public static string MinistryNewHref(int orgId) =>
        $"Organizations/{orgId}/Ministries/New";

    /// <summary>Team "new" page (under a league/ministry). <c>Leagues/{ministryId}/Teams/New</c>.</summary>
    public static string TeamNewHref(int ministryId) =>
        $"Leagues/{ministryId}/Teams/New";

    // ── Slot action helpers ──────────────────────────────────────────────

    /// <summary>Service-slot "new" page. <c>Organizations/{orgId}/Ministries/{ministryId}/Roles/New</c>.</summary>
    public static string SlotNewHref(int orgId, int ministryId) =>
        $"Organizations/{orgId}/Ministries/{ministryId}/Roles/New";

    // ── Organization action helpers ──────────────────────────────────────

    /// <summary>Organization "manage coordinators" page. <c>Organizations/{orgId}/Coordinators</c>.</summary>
    public static string OrganizationCoordinatorsHref(int orgId) =>
        $"Organizations/{orgId}/Coordinators";

    /// <summary>Organization "add stub member" page. <c>Organizations/{orgId}/Members/AddStub</c>.</summary>
    public static string OrganizationMemberAddStubHref(int orgId) =>
        $"Organizations/{orgId}/Members/AddStub";

    /// <summary>Organization "manage stub members" page. <c>Organizations/{orgId}/Members/Stubs</c>.</summary>
    public static string OrganizationStubsHref(int orgId) =>
        $"Organizations/{orgId}/Members/Stubs";

    /// <summary>Organization "training due soon" page. <c>Organizations/{orgId}/Training/DueSoon</c>.</summary>
    public static string OrganizationDueSoonHref(int orgId) =>
        $"Organizations/{orgId}/Training/DueSoon";

    /// <summary>Organization "training sessions" page. <c>Organizations/{orgId}/Training/Sessions</c>.</summary>
    public static string OrganizationSessionsHref(int orgId) =>
        $"Organizations/{orgId}/Training/Sessions";

    // ── Top-level route helpers (no id) ──────────────────────────────────

    /// <summary>Home route. Empty path so Blazor's router resolves it as the site root.</summary>
    public static string HomeHref() => "";

    /// <summary>Leagues index. <c>Leagues</c>.</summary>
    public static string LeaguesHref() => "Leagues";

    /// <summary>Training catalog manage page. <c>Training/Manage</c>.</summary>
    public static string TrainingManageHref() => "Training/Manage";

    /// <summary>Training "new content" page. <c>Training/New</c>.</summary>
    public static string TrainingNewHref() => "Training/New";

    /// <summary>Training "edit" page. <c>Training/{contentId}/Edit</c>.</summary>
    public static string TrainingEditHref(int trainingContentId) =>
        $"Training/{trainingContentId}/Edit";

    /// <summary>Organization "new" page. <c>Organizations/New</c>.</summary>
    public static string OrganizationNewHref() => "Organizations/New";

    // ── League-action helpers ───────────────────────────────────────────

    /// <summary>Game "new" page (under a league/ministry). <c>Leagues/{ministryId}/Games/New</c>.</summary>
    public static string GameNewHref(int ministryId) =>
        $"Leagues/{ministryId}/Games/New";

    /// <summary>Game detail page. <c>Games/{gameId}</c>.</summary>
    public static string GameHref(int gameId) => $"Games/{gameId}";

    /// <summary>Game "edit" page. <c>Games/{gameId}/Edit</c>.</summary>
    public static string GameEditHref(int gameId) => $"Games/{gameId}/Edit";

    // ── Team + standings helpers ─────────────────────────────────────────

    /// <summary>Team detail page. <c>Teams/{teamId}</c>.</summary>
    public static string TeamHref(int teamId) => $"Teams/{teamId}";

    /// <summary>League "full standings" page. <c>Leagues/{ministryId}/Standings</c>.</summary>
    public static string LeagueStandingsHref(int ministryId) =>
        $"Leagues/{ministryId}/Standings";

    /// <summary>League "schedule" page. <c>Leagues/{ministryId}/Schedule</c>.</summary>
    public static string LeagueScheduleHref(int ministryId) =>
        $"Leagues/{ministryId}/Schedule";
}
