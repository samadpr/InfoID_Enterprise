using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace InfoID.Infrastructure.Persistence;

public sealed class DatabaseDiagnosticsService : IDatabaseDiagnosticsService
{
    private readonly InfoIdDbContext _context;

    public DatabaseDiagnosticsService(InfoIdDbContext context)
    {
        _context = context;
    }

    public async Task<DatabaseHealthReport> CheckHealthAsync(CancellationToken ct = default)
    {
        var dbPath = _context.Database.GetDbConnection().DataSource;
        var fileExists = File.Exists(dbPath);

        bool canConnect;
        string? error = null;
        var applied = Array.Empty<string>().AsEnumerable();
        var pending = Array.Empty<string>().AsEnumerable();

        try
        {
            canConnect = await _context.Database.CanConnectAsync(ct);
            applied = await _context.Database.GetAppliedMigrationsAsync(ct);
            pending = await _context.Database.GetPendingMigrationsAsync(ct);
        }
        catch (Exception ex)
        {
            canConnect = false;
            error = ex.Message;
        }

        var isWritable = IsDirectoryWritable(Path.GetDirectoryName(dbPath));

        return new DatabaseHealthReport(
            DatabasePath: dbPath,
            FileExists: fileExists,
            CanConnect: canConnect,
            IsWritable: isWritable,
            AppliedMigrations: applied.ToList(),
            PendingMigrations: pending.ToList(),
            ErrorMessage: error);
    }

    private static bool IsDirectoryWritable(string? directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var probePath = Path.Combine(directory, $".infoid-write-probe-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "probe");
            File.Delete(probePath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}