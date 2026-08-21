using InfoID.Domain.Entities.SystemModule;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Infrastructure.Persistence;

/// <summary>
/// First-run and every-run database bring-up: creates the AppData\InfoID\Database folder
/// if missing, applies pending EF Core migrations (Database.Migrate() -- never
/// EnsureDeleted/EnsureCreated, per the no-destructive-recreation requirement), and runs
/// a minimal idempotent seed. Safe to call on every launch.
/// </summary>
public sealed class DatabaseInitializer : IDatabaseInitializer
{
    private readonly InfoIdDbContext _context;
    private readonly IStartupLogger _logger;

    public DatabaseInitializer(InfoIdDbContext context, IStartupLogger logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<DatabaseInitializationResult> InitializeAsync(CancellationToken ct = default)
    {
        var dbPath = _context.Database.GetDbConnection().DataSource;
        _logger.Info($"Database initialization started. Path: {dbPath}");

        try
        {
            var directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var fileExistedBefore = File.Exists(dbPath);

            var pending = (await _context.Database.GetPendingMigrationsAsync(ct)).ToList();
            if (pending.Count > 0)
            {
                _logger.Info($"Applying {pending.Count} pending migration(s): {string.Join(", ", pending)}");
            }
            else
            {
                _logger.Info("No pending migrations -- schema already current.");
            }

            await _context.Database.MigrateAsync(ct);

            var applied = (await _context.Database.GetAppliedMigrationsAsync(ct)).ToList();
            _logger.Info($"Migration step completed. {applied.Count} total migration(s) applied.");

            await SeedAsync(ct);

            _logger.Info("Database initialization completed successfully.");

            return new DatabaseInitializationResult(
                Success: true,
                DatabasePath: dbPath,
                DatabaseFileExistedBeforeInit: fileExistedBefore,
                AppliedMigrations: applied,
                ErrorMessage: null,
                Exception: null);
        }
        catch (Exception ex)
        {
            _logger.Error("Database initialization failed.", ex);

            return new DatabaseInitializationResult(
                Success: false,
                DatabasePath: dbPath,
                DatabaseFileExistedBeforeInit: File.Exists(dbPath),
                AppliedMigrations: Array.Empty<string>(),
                ErrorMessage: ex.Message,
                Exception: ex);
        }
    }

    /// <summary>
    /// Idempotent seed. Guarded by a SystemSetting marker row so re-running the app
    /// never duplicates data. Add real catalog seed data here as later phases introduce
    /// entities that need it (card formats, default printer profiles, etc.) -- always
    /// behind the same "does it already exist" check pattern shown below.
    /// </summary>
    private async Task SeedAsync(CancellationToken ct)
    {
        const string markerKey = "System.Seed.InitialSetupCompletedAt";

        var alreadySeeded = await _context.SystemSettings
            .AnyAsync(s => s.SettingKey == markerKey, ct);

        if (alreadySeeded)
        {
            return;
        }

        _context.SystemSettings.Add(new SystemSetting
        {
            OrganizationId = null, // machine-wide
            SettingKey = markerKey,
            SettingValue = DateTime.UtcNow.ToString("O"),
            CreatedDate = DateTime.UtcNow,
        });

        await _context.SaveChangesAsync(ct);
        _logger.Info("Initial seed applied.");
    }
}