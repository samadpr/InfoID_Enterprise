using InfoID.Licensing.Server.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace InfoID.AdminLicenseManager;

/// <summary>Lets `dotnet ef migrations add ...` create the context with the same
/// connection string the app uses (appsettings.json or INFOID_LICENSE_DB).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<LicenseDbContext>
{
    public LicenseDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var connectionString = Environment.GetEnvironmentVariable("INFOID_LICENSE_DB")
                               ?? configuration.GetConnectionString("LicenseDb")
                               ?? "Host=localhost;Database=infoid_license;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<LicenseDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("InfoID.Licensing.Server"))
            .Options;
        return new LicenseDbContext(options);
    }
}
