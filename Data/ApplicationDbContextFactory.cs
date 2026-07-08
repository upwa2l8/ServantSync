using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ServantSync.Data;

/// <summary>
/// Used by the EF Core CLI (`dotnet ef migrations add`, `dotnet ef database update`)
/// to construct <see cref="ApplicationDbContext"/> at design time without spinning
/// up the full Program.cs service provider. Without this, the EF tooling hits a
/// "Cannot resolve scoped service from root provider" error when the DI graph
/// contains scoped services (Identity stores, IWebHostEnvironment chain etc.) that
/// the design-time host cannot create a scope for.
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("EF_CONNECTION")
            ?? "Data Source=servantsync.db";

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}
