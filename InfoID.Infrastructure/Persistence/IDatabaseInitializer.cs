using System.Threading;
using System.Threading.Tasks;

namespace InfoID.Infrastructure.Persistence;

/// <summary>
/// Runs once at application startup, before any window is shown. Must be idempotent --
/// safe to run on every launch without duplicating seed data or throwing on rows that
/// already exist.
/// </summary>
public interface IDatabaseInitializer
{
    Task<DatabaseInitializationResult> InitializeAsync(CancellationToken ct = default);
}