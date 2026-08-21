using InfoID.Domain.Common.Interfaces;
using InfoID.Infrastructure.Persistence;
using InfoID.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers InfoIdDbContext, IUnitOfWork, the generic IRepository&lt;T&gt;, and the
    /// database initialization/diagnostics services with the DI container.
    ///
    /// databasePath: pass null in production to use the default per-user AppData location
    /// (recommended). Pass an explicit path only for tests or special deployment scenarios.
    /// The default itself now lives in one place -- see <see cref="DatabaseLocation"/> --
    /// instead of being duplicated between here and the design-time factory.
    /// </summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string? databasePath = null)
    {
        var path = databasePath ?? DatabaseLocation.GetDefaultDatabasePath();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        services.AddDbContext<InfoIdDbContext>(options =>
            options.UseSqlite($"Data Source={path}"));

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped(typeof(IRepository<>), typeof(Repository<>));

        services.AddSingleton<IStartupLogger, FileStartupLogger>();
        services.AddScoped<IDatabaseInitializer, DatabaseInitializer>();
        services.AddScoped<IDatabaseDiagnosticsService, DatabaseDiagnosticsService>();

        return services;
    }
}