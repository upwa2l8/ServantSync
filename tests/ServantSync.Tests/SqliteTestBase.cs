using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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

    /// <summary>
    /// Round-FR-3.2: a real <see cref="UserManager{IdentityUser}"/>
    /// backed by the in-memory SQLite database, so service tests that
    /// create placeholder IdentityUsers (e.g. <c>PersonService.CreateStubAsync</c>)
    /// can exercise the production code path. Constructed with the
    /// standard <see cref="UserOnlyStore{TUser, TContext}"/> EF store
    /// + the Identity defaults for password hashing / validators /
    /// normalizers / error describer / logger. The constructor mirrors
    /// what <c>AddIdentity&lt;IdentityUser, IdentityRole&gt;(...).AddEntityFrameworkStores&lt;ApplicationDbContext&gt;().AddDefaultTokenProviders()</c>
    /// builds in <c>Program.cs</c>.
    /// </summary>
    protected UserManager<IdentityUser> UserManager { get; }

    protected SqliteTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddDbContext<ApplicationDbContext>(opts =>
            opts.UseSqlite(_connection));
        var sp = services.BuildServiceProvider();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        Factory = new TestDbContextFactory(options);

        // Note: NOT in a `using` -- the dbContext instance is held alive
        // for the test's lifetime by the UserOnlyStore inside the
        // UserManager below. Disposing here would force
        // ObjectDisposedException on the next UserManager.CreateAsync
        // call from a service test (the service itself gets a fresh
        // db from the Factory, but the UserStore's dbContext is the
        // one constructed here).
        var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();

        // Build the UserManager on top of the same in-memory SQLite
        // connection so the placeholder IdentityUsers it creates are
        // visible to the DbContext the service uses.
        var userStore = new UserOnlyStore<IdentityUser>(db, /*describer*/ null!);
        // The default constructor wires up the standard
        // password hasher/validators/normalizers/logger — matches
        // what AddIdentity().AddEntityFrameworkStores().AddDefaultTokenProviders()
        // produces at runtime.
        UserManager = new UserManager<IdentityUser>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<IdentityUser>(),
            new IUserValidator<IdentityUser>[0],
            new IPasswordValidator<IdentityUser>[0],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            sp,
            sp.GetRequiredService<ILogger<UserManager<IdentityUser>>>());
    }

    public void Dispose() => _connection.Dispose();
}
