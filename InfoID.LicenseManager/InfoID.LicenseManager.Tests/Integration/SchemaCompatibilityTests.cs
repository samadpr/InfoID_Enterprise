using InfoID.LicenseManager.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using LegacyContext = InfoID.Licensing.Server.Data.LicenseDbContext;

namespace InfoID.LicenseManager.Tests.Integration;

/// <summary>Builds one database with the legacy InfoID.Licensing.Server migrations and one
/// with this project's copy of them, then compares the resulting schemas and migration
/// history. Proves an existing production database and a fresh one are interchangeable.</summary>
[Collection(PostgresCollection.Name)]
public class SchemaCompatibilityTests
{
    private readonly PostgresFixture _postgres;

    public SchemaCompatibilityTests(PostgresFixture postgres) => _postgres = postgres;

    [SkippableFact]
    public async Task Migrations_create_the_same_schema_as_the_legacy_license_server()
    {
        _postgres.SkipIfUnavailable();
        var legacyCs = await _postgres.CreateDatabaseAsync("legacy_schema");
        var newCs = await _postgres.CreateDatabaseAsync("new_schema");

        await MigrateLegacyAsync(legacyCs);
        await using (var current = new LicenseManagerDbContext(new DbContextOptionsBuilder<LicenseManagerDbContext>()
                         .UseNpgsql(newCs).Options))
            await current.Database.MigrateAsync();

        Assert.Equal(await DescribeSchemaAsync(legacyCs), await DescribeSchemaAsync(newCs));

        const string history = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY 1";
        Assert.Equal(await QueryAsync(legacyCs, history), await QueryAsync(newCs, history));
    }

    [SkippableFact]
    public async Task A_database_created_by_the_legacy_migrations_needs_no_further_migration()
    {
        _postgres.SkipIfUnavailable();
        var cs = await _postgres.CreateDatabaseAsync("legacy_then_new");
        await MigrateLegacyAsync(cs);

        await using var current = new LicenseManagerDbContext(
            new DbContextOptionsBuilder<LicenseManagerDbContext>().UseNpgsql(cs).Options);

        Assert.Empty(await current.Database.GetPendingMigrationsAsync());
    }

    private static async Task MigrateLegacyAsync(string connectionString)
    {
        await using var legacy = new LegacyContext(new DbContextOptionsBuilder<LegacyContext>()
            .UseNpgsql(connectionString).Options);
        await legacy.Database.MigrateAsync();
    }

    private static async Task<List<string>> DescribeSchemaAsync(string connectionString)
    {
        var lines = new List<string>();
        lines.AddRange(await QueryAsync(connectionString,
            "SELECT table_name || '.' || column_name || ' ' || data_type || ' null=' || is_nullable || ' def=' || COALESCE(column_default, '') || ' len=' || COALESCE(character_maximum_length::text, '') " +
            "FROM information_schema.columns WHERE table_schema = 'public' ORDER BY 1"));
        lines.AddRange(await QueryAsync(connectionString,
            "SELECT indexdef FROM pg_indexes WHERE schemaname = 'public' ORDER BY 1"));
        lines.AddRange(await QueryAsync(connectionString,
            "SELECT conrelid::regclass || ' ' || conname || ' ' || pg_get_constraintdef(oid) FROM pg_constraint WHERE connamespace = 'public'::regnamespace ORDER BY 1"));
        return lines;
    }

    private static async Task<List<string>> QueryAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return result;
    }
}
