using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;

namespace ServantSync.Tests;

/// <summary>
/// Builder helpers for the test seed data. Each method returns the new
/// entity AND inserts it via the given factory so the caller doesn't
/// have to remember to call SaveChanges. Required FKs (e.g.
/// ServiceSlot.MinistryId) are taken from the supplied parent entity.
/// </summary>
public static class TestData
{
    // Counter so the "user id" strings are distinct across calls within
    // a single test (the IdentityUser.Id is also the Person.UserId, but
    // for tests we just need distinct, non-empty strings).
    // Note: <c>Interlocked.Increment</c> means two calls in the same
    // test produce two DIFFERENT users — passing the same <c>userId</c>
    // argument explicitly is the only way to share identity between
    // Person rows.
    private static int _userCounter = 0;

    public static Organization Org(IDbContextFactory<Data.ApplicationDbContext> factory, string name = "Test Org") =>
        Save(factory, () => new Organization
        {
            Name = name,
            ContactEmail = $"{name.ToLowerInvariant().Replace(' ', '-')}@test.local",
        });

    public static Ministry Ministry(IDbContextFactory<ApplicationDbContext> factory, int orgId, string name = "Test Ministry") =>
        Save(factory, () => new Ministry
        {
            OrganizationId = orgId,
            Name = name,
        });

    public static ServiceSlot Slot(IDbContextFactory<Data.ApplicationDbContext> factory, int ministryId, string name = "Test Slot") =>
        Save(factory, () => new ServiceSlot
        {
            MinistryId = ministryId,
            Name = name,
            IsActive = true,
        });

    public static Person Person(IDbContextFactory<ApplicationDbContext> factory, string? firstName = null, string? lastName = null, string? userId = null)
    {
        var n = System.Threading.Interlocked.Increment(ref _userCounter);
        var id = userId ?? $"user-{n}";
        var first = firstName ?? $"Test{n}";
        var last = lastName ?? $"User{n}";
        // Person.UserId is a 1:1 FK to IdentityUser.Id (PK), so we have
        // to create the IdentityUser row first or the Person insert fails
        // the FK constraint check.
        EnsureIdentityUser(factory, id);
        return Save(factory, () => new Person
        {
            UserId = id,
            FirstName = first,
            LastName = last,
        });
    }

    /// <summary>Creates an IdentityUser row (idempotent on re-runs).</summary>
    private static void EnsureIdentityUser(IDbContextFactory<ApplicationDbContext> factory, string userId)
    {
        using var db = factory.CreateDbContext();
        if (db.Users.Any(u => u.Id == userId)) return;
        db.Users.Add(new IdentityUser
        {
            Id = userId,
            UserName = $"{userId}@test.local",
            Email = $"{userId}@test.local",
            EmailConfirmed = true,
        });
        db.SaveChanges();
    }

    public static OrganizationMembership Membership(
        IDbContextFactory<ApplicationDbContext> factory,
        string userId, int orgId, OrganizationRole role) =>
        Save(factory, () => new OrganizationMembership
        {
            PersonUserId = userId,
            OrganizationId = orgId,
            Role = role,
        });

    /// <summary>
    /// Creates a TrainingContent scoped to <paramref name="orgId"/>. Since round N
    /// every TrainingContent must belong to one Organization, this helper
    /// makes <paramref name="orgId"/> a required parameter; existing callers
    /// were updated to pass the owning org id explicitly so the schema's
    /// NOT NULL + FK constraints don't trip the test fixtures.
    /// </summary>
    public static TrainingContent TrainingContent(
        IDbContextFactory<ApplicationDbContext> factory,
        int orgId,
        string title = "Safe Spaces 101") =>
        Save(factory, () => new TrainingContent
        {
            OrganizationId = orgId,
            Title = title,
            Description = "Test training content.",
            Format = TrainingFormat.Video,
            FilePathOrUrl = "https://example.com/training",
            Version = 1,
        });

    /// <summary>
    /// Round M helper: TrainingContent as an uploaded PDF with an
    /// explicit <see cref="TrainingContent.TotalPageCount"/> so the
    /// "view every page" eligibility rule has a denominator. Pre-fill
    /// is mandatory because PDF page counts are normally extracted
    /// server-side from the uploaded file (and we don't actually
    /// upload files in tests).
    /// </summary>
    public static TrainingContent PdfContent(
        IDbContextFactory<ApplicationDbContext> factory,
        int orgId,
        string title = "PDF Guide",
        int? totalPages = 1) =>
        Save(factory, () => new TrainingContent
        {
            OrganizationId = orgId,
            Title = title,
            Description = "Test PDF training content.",
            Format = TrainingFormat.Pdf,
            FilePathOrUrl = "/uploads/training/test.pdf",
            TotalPageCount = totalPages,
            Version = 1,
        });

    public static TrainingRequirement Requirement(
        IDbContextFactory<ApplicationDbContext> factory,
        int contentId,
        int? orgId = null,
        int? slotId = null,
        TrainingCadence cadence = TrainingCadence.Yearly,
        int? cadenceMonths = null) =>
        Save(factory, () => new TrainingRequirement
        {
            TrainingContentId = contentId,
            OrganizationId = orgId,
            ServiceSlotId = slotId,
            Cadence = cadence,
            CadenceMonths = cadenceMonths,
        });

    /// <summary>Records a completion that is valid for one year from <paramref name="completionUtc"/>.</summary>
    /// <param name="contentVersion">
    /// Snapshot of the TrainingContent version this completion is for.
    /// Defaults to 1 to keep older callers working. A volunteer who has
    /// retaken the same training across multiple content versions
    /// (i.e. after a content refresh) will need distinct version values
    /// because the schema's UNIQUE index on
    /// <c>(PersonUserId, TrainingContentId, TrainingContentVersion)</c>
    /// would otherwise collide.
    /// </param>
    public static TrainingCompletion Completion(
        IDbContextFactory<ApplicationDbContext> factory,
        string userId, int contentId, DateTime completionUtc, int contentVersion = 1) =>
        Save(factory, () => new TrainingCompletion
        {
            PersonUserId = userId,
            TrainingContentId = contentId,
            TrainingContentVersion = contentVersion,
            CompletionUtc = completionUtc,
            ExpiresUtc = completionUtc.AddYears(1),
        });

    public static Team Team(IDbContextFactory<ApplicationDbContext> factory, int ministryId, string name = "Test Team",
        TeamAgeBracket bracket = TeamAgeBracket.U10, string? coachUserId = null) =>
        Save(factory, () => new Team
        {
            MinistryId = ministryId,
            Name = name,
            AgeBracket = bracket,
            CoachPersonUserId = coachUserId,
        });

    public static Arena Arena(IDbContextFactory<ApplicationDbContext> factory, int orgId, string name = "Field 1") =>
        Save(factory, () => new Arena
        {
            OrganizationId = orgId,
            Name = name,
            SurfaceType = "Grass",
        });

    public static Assignment Assignment(
        IDbContextFactory<ApplicationDbContext> factory,
        string userId, int slotId, DateTime startUtc, DateTime endUtc,
        AssignmentStatus status = AssignmentStatus.Scheduled) =>
        Save(factory, () => new Assignment
        {
            PersonUserId = userId,
            ServiceSlotId = slotId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Status = status,
        });

    public static Game Game(
        IDbContextFactory<ApplicationDbContext> factory,
        int ministryId, int homeTeamId, int awayTeamId, int arenaId,
        DateTime startUtc, DateTime endUtc,
        GameStatus status = GameStatus.Scheduled) =>
        Save(factory, () => new Game
        {
            MinistryId = ministryId,
            HomeTeamId = homeTeamId,
            AwayTeamId = awayTeamId,
            ArenaId = arenaId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Status = status,
        });

    private static T Save<T>(IDbContextFactory<ApplicationDbContext> factory, Func<T> factory2) where T : class
    {
        using var db = factory.CreateDbContext();
        var entity = factory2();
        db.Set<T>().Add(entity);
        db.SaveChanges();
        return entity;
    }
}
