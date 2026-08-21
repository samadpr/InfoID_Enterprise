using System.Threading;
using System.Threading.Tasks;

namespace InfoID.Infrastructure.Persistence;

/// <summary>
/// On-demand health check -- for a future Settings/About "Database" panel and for
/// support diagnostics. Does not modify anything.
/// </summary>
public interface IDatabaseDiagnosticsService
{
    Task<DatabaseHealthReport> CheckHealthAsync(CancellationToken ct = default);
}