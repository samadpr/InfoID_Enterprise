using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Infrastructure.Persistence;
using InfoID.LicenseManager.Tests.Fakes;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace InfoID.LicenseManager.Tests.Integration;

/// <summary>
/// A throwaway PostgreSQL server in Docker. Integration tests create their own databases on it and
/// never touch the real infoid_license database. When Docker is not running the tests that need it
/// are reported as SKIPPED (with the reason), never as passed.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private readonly List<string> _createdDatabases = new();
    private readonly string _runId = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Optional: set INFOID_TEST_POSTGRES to the admin connection string of any PostgreSQL server (for example a
    /// local install) to run the integration tests there instead of in Docker. Only throwaway databases named
    /// "lm_test_..." are created and they are dropped again at the end; no existing database is touched.</summary>
    private static string? ExternalServer => Environment.GetEnvironmentVariable("INFOID_TEST_POSTGRES");

    public string? UnavailableReason { get; private set; }

    public async Task InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(ExternalServer)) return;

        try
        {
            _container = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
            await _container.StartAsync();
        }
        catch (Exception ex)
        {
            UnavailableReason = "PostgreSQL test container could not start (is Docker Desktop running?): " + ex.GetBaseException().Message;
        }
    }

    public async Task DisposeAsync()
    {
        if (ExternalServer is { } externalServer)
        {
            // Best effort: a throwaway database that cannot be dropped must not fail the run (the next run's names differ).
            // Failures are written to %TEMP%\pg-cleanup.log, and "lm_test_*" databases can be dropped by hand.
            NpgsqlConnection.ClearAllPools();
            var builder = new NpgsqlConnectionStringBuilder(externalServer) { CommandTimeout = 120, Pooling = false };
            foreach (var name in _createdDatabases)
            {
                try
                {
                    await using var connection = new NpgsqlConnection(builder.ConnectionString);
                    await connection.OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = $"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)";
                    await command.ExecuteNonQueryAsync();
                }
                catch (Exception ex)
                {
                    File.AppendAllText(Path.Combine(Path.GetTempPath(), "pg-cleanup.log"), $"{DateTime.Now:s} {name}: {ex.GetBaseException().Message}{Environment.NewLine}");
                }
            }
        }

        if (_container is not null) await _container.DisposeAsync();
    }

    public void SkipIfUnavailable() => Skip.If(UnavailableReason is not null, UnavailableReason);

    /// <summary>Creates an empty database and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(string name)
    {
        SkipIfUnavailable();
        var admin = ExternalServer ?? _container!.GetConnectionString();
        NpgsqlConnection.ClearAllPools(); // idle connections of earlier tests would otherwise add up past the server connection limit
        var unique = ExternalServer is null ? name : $"lm_test_{_runId}_{name}";
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{unique}\"";
            await command.ExecuteNonQueryAsync();
        }

        _createdDatabases.Add(unique);
        return new NpgsqlConnectionStringBuilder(admin) { Database = unique, MaxPoolSize = 60, ConnectionPruningInterval = 1, ConnectionIdleLifetime = 2 }.ConnectionString;
    }

    /// <summary>Creates a database and applies the application's migrations to it.</summary>
    public async Task<string> CreateMigratedDatabaseAsync(string name)
    {
        var connectionString = await CreateDatabaseAsync(name);
        await using var db = NewContext(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    public static LicenseManagerDbContext NewContext(string connectionString, ICurrentUser? user = null) =>
        new(new DbContextOptionsBuilder<LicenseManagerDbContext>().UseNpgsql(connectionString).Options, user ?? new FakeCurrentUser());
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
