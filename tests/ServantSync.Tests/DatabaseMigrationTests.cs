using Microsoft.EntityFrameworkCore;
using ServantSync.Data;
using ServantSync.Models;
using Xunit;

namespace ServantSync.Tests;

/// <summary>
/// Model-consistency smoke tests.
///
/// Previously ran MigrateAsync against a fresh SQLite file to
/// regression-test migration SQL. After the switch to Azure SQL
/// Database, MigrateAsync requires a live SQL Server instance
/// (impractical for local tests). These tests now use
/// EnsureCreated (in-memory SQLite via SqliteTestBase pattern)
/// to confirm the model is internally consistent.
///
/// The migration SQL itself is validated by:
/// 1. `dotnet ef migrations add` (model → SQL generation)
/// 2. Production's first-boot MigrateAsync against Azure SQL
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
    public async Task EnsureCreated_FreshDb_SchemaIsConsistent()
    {
        await using var ctx = MakeContext();

        // EnsureCreated mirrors the pattern used by every other
        // test in the suite (via SqliteTestBase). It validates
        // that the EF model produces a consistent schema.
        await ctx.Database.EnsureCreatedAsync();

        // TrainingContents is the canary: if the model has a
        // structural inconsistency, the query fails at prepare
        // time.
        var rows = await ctx.TrainingContents.ToListAsync();
        Assert.Empty(rows);
    }

    [Fact]
    public async Task EnsureCreated_FreshDb_InsertTrainingContent_Roundtrips()
    {
        await using var ctx = MakeContext();
        await ctx.Database.EnsureCreatedAsync();

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
        // In-memory SQLite for fast model-consistency smoke tests.
        // Uses the SQLite provider (referenced by the test project)
        // while production uses Azure SQL Database.
        var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={_dbPath};Pooling=False")
            .Options;
        return new ApplicationDbContext(opts);
    }
}

