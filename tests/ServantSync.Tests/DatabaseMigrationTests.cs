using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Regression coverage for the EF Core migration path.
///
/// The per-test fixtures elsewhere in this project use
/// <c>EnsureCreated</c>, never <c>Migrate</c>. So any SQLite-specific
/// bug in a hand-rolled <c>migrationBuilder.Sql(...)</c> would only
/// surface at first-boot time on a real machine — the test suite never
/// parsed the SQL. Today's bug (the
/// <c>20260705020246_AddTrainingContentOrganization</c> Phase-2 backfill
/// referenced <c>s.Ministry.OrganizationId</c> without joining the
/// <c>Ministries</c> table) is a representative example: SQLite threw
/// "no such column: s.Ministry.OrganizationId" and the migration
/// crashed before any <c>OrganizationId</c> made it onto
/// <c>TrainingContents</c>, which in turn made the seeder fail with
/// "no such column: OrganizationId" downstream.
///
/// This single test takes the actual <c>dotnet ef database update</c>
/// path — build a context with no factory, point it at a fresh
/// SQLite file, call <c>MigrateAsync</c>, and assert every migration
/// applied + <c>TrainingContents</c> is queryable (which is sufficient
/// to confirm <c>OrganizationId</c> is in the schema, because the EF
/// model demands the column). One assertion per migration is the
/// cheapest regression we can buy against this category of bug.
/// </summary>
public class DatabaseMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public DatabaseMigrationTests()
    {
        _dbPath = Path.Combine(
            Path.GetTempPath(),
            $"servantsync_migrationtest_{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        // With `Pooling=False` the connection closes via
        // `await using ctx` before this runs, so Delete is safe.
        // Surface any unusual cleanup failures as test failures
        // rather than swallowing them — more diagnostic.
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Fact]
    public async Task Migrate_FreshDb_AppliesAllMigrations()
    {
        // Build a context directly (bypass the per-test fixtures
        // which use EnsureCreated) so we run the real EF Core
        // migration pipeline, mirroring `dotnet ef database update`
        // / `dotnet run` first-boot exactly.
        await using var ctx = MakeContext();

        // The actual smoke test — used to throw `SQLite Error 1:
        // no such column: s.MINISTRY.ORGANIZATIONID` when the
        // Phase-2 backfill CTE used an EF navigation property
        // without joining the underlying table.
        await ctx.Database.MigrateAsync();

        // We assert via applied/pending rather than a hardcoded
        // count so the test tracks the assembly as it grows.
        // A fixed integer breaks the build every time a new
        // migration ships — easy to forget to update.
        var pending = await ctx.Database.GetPendingMigrationsAsync();
        Assert.Empty(pending);

        // The EF model requires `OrganizationId` on TrainingContents;
        // if the column were missing, the SELECT below would fail
        // at SQLite prepare time. So a successful empty query
        // is sufficient proof the schema is consistent with the
        // current model.
        var rows = await ctx.TrainingContents.ToListAsync();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task Migrate_FreshDb_InsertTrainingContent_Roundtrips()
    {
        // Defensive deeper coverage: confirm a TrainingContent insert
        // round-trips on a fully-migrated schema so the cascade FK
        // to Organizations is also exercised end-to-end.
        await using var ctx = MakeContext();
        await ctx.Database.MigrateAsync();

        // Save the org FIRST so `org.Id` is populated before the
        // content assigns its FK. Setting `OrganizationId = org.Id`
        // while `org.Id` is still 0 would insert with FK = 0 and
        // trip the Organizations FK constraint.
        var org = new Organization
        {
            Name = "Test Org",
            RegistrationToken = Guid.NewGuid().ToString("N"),
        };
        ctx.Organizations.Add(org);
        await ctx.SaveChangesAsync();

        var content = new TrainingContent
        {
            OrganizationId = org.Id,
            Title = "Smoke Test Training",
            Format = TrainingFormat.Video,
            FilePathOrUrl = "https://example.com",
            Version = 1,
            VersionDateUtc = DateTime.UtcNow,
        };
        ctx.TrainingContents.Add(content);
        await ctx.SaveChangesAsync();

        Assert.True(content.Id > 0);
        Assert.Equal(org.Id, content.OrganizationId);

        var fetched = await ctx.TrainingContents
            .Include(c => c.Organization)
            .FirstAsync(c => c.Id == content.Id);
        Assert.Equal("Smoke Test Training", fetched.Title);
        Assert.Equal("Test Org", fetched.Organization.Name);
    }

    private ApplicationDbContext MakeContext()
    {
        // Pooling=False avoids the global connection pool entirely,
        // so Dispose can File.Delete the per-test database without
        // disturbing any other SQLite-using test in the same xUnit
        // collection. Mirrors `dotnet ef database update` against
        // a real file.
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=False")
            .Options;
        return new ApplicationDbContext(opts);
    }
}

