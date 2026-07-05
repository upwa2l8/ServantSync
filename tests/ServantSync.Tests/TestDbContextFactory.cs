using Microsoft.EntityFrameworkCore;
using ServantSync.Data;

namespace ServantSync.Tests;

/// <summary>
/// Test <see cref="IDbContextFactory{TContext}"/> that hands out
/// <see cref="ApplicationDbContext"/> instances backed by a shared
/// <see cref="DbContextOptions{TContext}"/>. Used with a
/// <see cref="Microsoft.Data.Sqlite.SqliteConnection"/> opened against
/// <c>DataSource=:memory:</c> so each test gets a fresh in-memory
/// database with the schema created by
/// <c>db.Database.EnsureCreated()</c>.
/// </summary>
public class TestDbContextFactory : IDbContextFactory<ApplicationDbContext>
{
    private readonly DbContextOptions<ApplicationDbContext> _options;

    public TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
    {
        _options = options;
    }

    public ApplicationDbContext CreateDbContext() => new(_options);

    public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken ct = default) =>
        Task.FromResult(new ApplicationDbContext(_options));
}
