using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InfoID.Infrastructure;

/// <summary>
/// Design-time factory used only by the `dotnet ef` CLI (migrations add/update)
/// -- it never runs inside the shipped app. Points at a local ./Database folder
/// relative to wherever `dotnet ef` is invoked from (normally this project's
/// folder), which is fine for development.
///
/// The real app does NOT use this factory -- InfoID.App's startup calls
/// AddInfrastructure() (see DependencyInjection.cs) which resolves a proper
/// per-user AppData path at runtime. Using a relative path in production would
/// break the moment the app is launched from a different working directory
/// (e.g. a desktop shortcut vs. a terminal), which is why the two are kept
/// deliberately separate.
/// </summary>
public class InfoIdDbContextFactory : IDesignTimeDbContextFactory<InfoIdDbContext>
{
    public InfoIdDbContext CreateDbContext(string[] args)
    {
        Directory.CreateDirectory("Database");

        var optionsBuilder = new DbContextOptionsBuilder<InfoIdDbContext>();
        optionsBuilder.UseSqlite("Data Source=Database/InfoID_DB.db");

        return new InfoIdDbContext(optionsBuilder.Options);
    }
}
