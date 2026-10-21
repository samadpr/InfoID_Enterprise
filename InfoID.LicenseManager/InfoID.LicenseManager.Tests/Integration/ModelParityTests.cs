using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using LegacyContext = InfoID.Licensing.Server.Data.LicenseDbContext;

namespace InfoID.LicenseManager.Tests.Integration;

/// <summary>The EF model must describe exactly the schema the existing migrations
/// create, otherwise the next "migrations add" would try to alter a production
/// database. No database connection is needed for these checks.</summary>
public class ModelParityTests
{
    private static LicenseManagerDbContext CreateContext() => new(
        new DbContextOptionsBuilder<LicenseManagerDbContext>()
            .UseNpgsql("Host=localhost;Database=never_connected;Username=x;Password=x")
            .Options);

    [Fact]
    public void Current_model_has_no_changes_compared_with_the_migration_snapshot()
    {
        using var db = CreateContext();

        Assert.False(db.Database.HasPendingModelChanges(),
            "The EF model differs from the migration snapshot inherited from InfoID.Licensing.Server.");
    }

    [Fact]
    public void Migration_sql_is_identical_to_the_legacy_license_server_sql()
    {
        const string cs = "Host=localhost;Database=never_connected;Username=x;Password=x";
        using var legacy = new LegacyContext(new DbContextOptionsBuilder<LegacyContext>().UseNpgsql(cs).Options);
        using var current = CreateContext();

        var legacySql = legacy.GetService<IMigrator>().GenerateScript();
        var currentSql = current.GetService<IMigrator>().GenerateScript();

        Assert.False(string.IsNullOrWhiteSpace(currentSql));
        Assert.Equal(legacySql, currentSql);
    }

    [Fact]
    public void Original_migration_ids_are_preserved_so_existing_databases_stay_compatible()
    {
        using var db = CreateContext();

        Assert.Equal(
            new[]
            {
                "20260928132735_InitialCreate",
                "20260930093550_AddOnlineActivation",
                "20261001100805_AddCustomerPlatform",
                "20261012085219_AddTrialUsage",
            },
            db.Database.GetMigrations().ToArray());
    }
}
