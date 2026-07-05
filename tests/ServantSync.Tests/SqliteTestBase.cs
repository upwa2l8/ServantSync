using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServantSync.Data;

namespace ServantSync.Tests;

/// <summary>
/// Base class for service-integration tests. Each test inherits a fresh
/// in-memory SQLite database (via <see cref="SqliteConnection"/> with
/// <c>DataSource=:memory:</c>); the schema is materialized with
/// <c>EnsureCreated()</c> so tests don't depend on the migration history.
///
/// The connection is opened eagerly (required for in-memory SQLite) and
/// kept alive for the lifetime of the test — closing it would destroy
/// the database. <see cref="Dispose"/> closes the connection and drops
/// the database.
/// </summary>
public abstract class SqliteTestBase : IDisposable
{
    private readonly SqliteConnection _connection;

    /// <summary>The factory the service-under-test depends on.</summary>
    protected IDbContextFactory<ApplicationDbContext> Factory { get; }

    protected SqliteTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        Factory = new TestDbContextFactory(options);

        using var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();
}
