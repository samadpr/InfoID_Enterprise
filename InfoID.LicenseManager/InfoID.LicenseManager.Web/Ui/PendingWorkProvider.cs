using InfoID.LicenseManager.Application.Interfaces.Services;

namespace InfoID.LicenseManager.Web.Ui;

/// <summary>The "work waiting" number in the sidebar and the header bell: license requests that are new or in progress.
/// It is a convenience on every page, so a failure to count (database trouble) is logged and shown as nothing
/// instead of breaking the page - including the error page itself.</summary>
public interface IPendingWorkProvider
{
    Task<int> GetPendingRequestsAsync(CancellationToken cancellationToken = default);
}

public sealed class PendingWorkProvider : IPendingWorkProvider
{
    private readonly IDashboardAnalyticsService _analytics;
    private readonly ILogger<PendingWorkProvider> _logger;
    private int? _cached;

    public PendingWorkProvider(IDashboardAnalyticsService analytics, ILogger<PendingWorkProvider> logger)
    {
        _analytics = analytics;
        _logger = logger;
    }

    public async Task<int> GetPendingRequestsAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is { } known) return known;
        try
        {
            _cached = await _analytics.GetPendingRequestCountAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not count pending license requests for the page header");
            _cached = 0;
        }

        return _cached.Value;
    }
}
