using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InfoID.LicenseManager.Infrastructure.Persistence;

/// <summary>Lets <c>dotnet ef</c> create the context from this project alone. The
/// connection string comes from INFOID_LICENSE_DB; the fallback has no password and is
/// only good for commands that never connect (e.g. <c>migrations add</c>).</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LicenseManagerDbContext>
{
    public LicenseManagerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("INFOID_LICENSE_DB")
                               ?? "Host=localhost;Database=infoid_license;Username=infoid_admin";

        var options = new DbContextOptionsBuilder<LicenseManagerDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new LicenseManagerDbContext(options);
    }
}
