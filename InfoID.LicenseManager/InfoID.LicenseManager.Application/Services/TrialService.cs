using InfoID.LicenseManager.Application.DTOs.Trial;
using InfoID.LicenseManager.Application.Interfaces.Repositories;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.LicenseManager.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace InfoID.LicenseManager.Application.Services;

public sealed class TrialService : ITrialService
{
    private const string UnknownJob = "That print job isn't known to the server.";

    private readonly ITrialRepository _trials;
    private readonly TrialOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<TrialService> _logger;

    public TrialService(ITrialRepository trials, TrialOptions options, TimeProvider time, ILogger<TrialService> logger)
    {
        _trials = trials;
        _options = options;
        _time = time;
        _logger = logger;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    public async Task<TrialStatusDto> GetStatusAsync(long accountId, CancellationToken cancellationToken = default)
    {
        var usageId = await PrepareUsageAsync(accountId, cancellationToken);
        return await _trials.ReadStatusAsync(usageId, cancellationToken);
    }

    public async Task<TrialReserveResultDto> ReserveAsync(long accountId, int cards, string? machineFingerprint, string? appVersion, string? designName,
        CancellationToken cancellationToken = default)
    {
        var usageId = await PrepareUsageAsync(accountId, cancellationToken);

        var maxPerJob = Math.Max(1, _options.MaxCardsPerReservation);
        if (cards < 1 || cards > maxPerJob)
        {
            return new TrialReserveResultDto(false, null, await _trials.ReadStatusAsync(usageId, cancellationToken),
                cards < 1 ? "Nothing to print." : $"A trial print job can have at most {maxPerJob} cards.");
        }

        var now = UtcNow;
        var key = Guid.NewGuid();
        var expires = now.AddMinutes(Math.Max(1, _options.ReservationMinutes));

        // The whole limit check is one conditional update inside the repository.
        if (!await _trials.TryReserveAsync(usageId, accountId, cards, key, expires, now, Truncate(designName, 200),
                Truncate(machineFingerprint, 100), Truncate(appVersion, 50), cancellationToken))
        {
            var status = await _trials.ReadStatusAsync(usageId, cancellationToken);
            var error = status.Remaining == 0
                ? $"You've used all {status.Limit} trial cards. Activate InfoID to keep printing."
                : $"Only {status.Remaining} trial card{(status.Remaining == 1 ? "" : "s")} left. Print fewer cards, or activate InfoID.";
            return new TrialReserveResultDto(false, null, status, error);
        }

        _logger.LogInformation("Trial reservation {ReservationKey}: {Cards} card(s) for account {AccountId}", key, cards, accountId);
        return new TrialReserveResultDto(true, key, await _trials.ReadStatusAsync(usageId, cancellationToken), null, expires);
    }

    public async Task<TrialCompleteResultDto> CommitAsync(long accountId, Guid reservationKey, int cardsPrinted, CancellationToken cancellationToken = default)
    {
        var usageId = await _trials.CommitAsync(accountId, reservationKey, Math.Max(0, cardsPrinted), UtcNow, cancellationToken);
        if (usageId is null) return new TrialCompleteResultDto(false, new TrialStatusDto(0, 0, 0), UnknownJob);

        _logger.LogInformation("Trial reservation {ReservationKey} committed", reservationKey);
        return new TrialCompleteResultDto(true, await _trials.ReadStatusAsync(usageId.Value, cancellationToken), null);
    }

    public async Task<TrialCompleteResultDto> ReleaseAsync(long accountId, Guid reservationKey, CancellationToken cancellationToken = default)
    {
        var usageId = await _trials.ReleaseAsync(accountId, reservationKey, UtcNow, cancellationToken);
        return usageId is null
            ? new TrialCompleteResultDto(false, new TrialStatusDto(0, 0, 0), UnknownJob)
            : new TrialCompleteResultDto(true, await _trials.ReadStatusAsync(usageId.Value, cancellationToken), null);
    }

    private async Task<long> PrepareUsageAsync(long accountId, CancellationToken cancellationToken)
    {
        var usageId = await _trials.EnsureUsageAsync(accountId, ProductCodes.InfoID, Math.Max(0, _options.CardLimit), cancellationToken);
        var expired = await _trials.ExpireStaleAsync(usageId, UtcNow, cancellationToken);
        if (expired > 0) _logger.LogWarning("{Count} trial reservation(s) expired without a result for account {AccountId}", expired, accountId);
        return usageId;
    }

    private static string? Truncate(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length > max ? value[..max] : value.Trim();
}
