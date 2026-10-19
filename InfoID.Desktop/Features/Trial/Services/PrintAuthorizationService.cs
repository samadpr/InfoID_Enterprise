using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Features.Account.Services;
using InfoID.Infrastructure.Persistence;
using InfoID.Shared.Contracts.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Trial.Services;

public enum PrintDenial
{
    None,
    /// <summary>No license and not signed in: log in to start the trial.</summary>
    SignInRequired,
    /// <summary>All trial cards are used.</summary>
    TrialUsedUp,
    /// <summary>Fewer trial cards left than this job needs.</summary>
    TrialNotEnough,
    /// <summary>Trial printing needs the server, which can't be reached.</summary>
    ServerUnavailable,
    /// <summary>The activated license doesn't allow it (expired, card limit...).</summary>
    LicenseProblem,
}

/// <summary>
/// Permission for ONE print job, from <see cref="PrintAuthorizationService"/>. The
/// printing code renders and sends pages only with an allowed authorization in hand, and
/// takes the watermark setting from it (never from a UI flag). Only the service can
/// create one; it is used once (completed or cancelled).
/// </summary>
public sealed class PrintAuthorization
{
    internal PrintAuthorization(bool allowed, bool isTrial, int cards, PrintDenial denial, string? message, Guid? reservationId = null)
    {
        IsAllowed = allowed;
        IsTrial = isTrial;
        Cards = cards;
        Denial = denial;
        Message = message;
        ReservationId = reservationId;
    }

    public bool IsAllowed { get; }
    /// <summary>Licensed printing: whether the printed cards use up the license's card
    /// allowance (false for visitor badges). Trial cards are always counted.</summary>
    public bool CountsTowardCardLimit { get; internal init; } = true;
    /// <summary>Trial print: watermark on, credits reserved on the server.</summary>
    public bool IsTrial { get; }
    /// <summary>Trial cards carry the "INFOID TRIAL VERSION" watermark.</summary>
    public bool Watermark => IsTrial;
    /// <summary>Physical cards this authorization covers.</summary>
    public int Cards { get; }
    public PrintDenial Denial { get; }
    /// <summary>Why printing isn't allowed (null when it is).</summary>
    public string? Message { get; }
    internal Guid? ReservationId { get; }
    internal bool Finished { get; set; }
}

/// <summary>
/// Decides whether a print job may run, and accounts for it afterwards:
///   activated license -> the license rules (FR-LIC-12) and its card counter;
///   signed in, no license -> trial: the cards are RESERVED on the server first (the
///     server refuses beyond the account's limit, atomically, so parallel jobs or PCs
///     can't exceed it), then COMMITTED with the number actually printed, or RELEASED if
///     the job failed or was cancelled;
///   neither -> denied, sign in first.
/// Used by every printing entry point (Print, Digital Copy, automatic digital copy).
/// </summary>
public sealed class PrintAuthorizationService
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<PrintAuthorizationService>();
    private static readonly SemaphoreSlim PendingGate = new(1, 1);

    private readonly IServiceScopeFactory _scopes;
    private readonly AccountSession _session;
    private readonly CustomerPortalClient _portal;
    private readonly TrialModeState _state;
    private readonly string _pendingPath;

    public PrintAuthorizationService(IServiceScopeFactory scopes, AccountSession session, CustomerPortalClient portal, TrialModeState state)
        : this(scopes, session, portal, state, Path.Combine(DatabaseLocation.GetApplicationRoot(), "PendingTrialCommits.json"))
    {
    }

    internal PrintAuthorizationService(IServiceScopeFactory scopes, AccountSession session, CustomerPortalClient portal, TrialModeState state, string pendingPath)
    {
        _scopes = scopes;
        _session = session;
        _portal = portal;
        _state = state;
        _pendingPath = pendingPath;
    }

    public static string SignInMessage(int limit) => $"Please log in to start your trial and print up to {limit} cards.";

    /// <summary>Asks permission to print <paramref name="cards"/> physical cards.
    /// <paramref name="feature"/> is the licensed feature being used: Printing (counts
    /// toward the card limit) or VisitorBadges (license must be valid, no counting).</summary>
    public async Task<PrintAuthorization> AuthorizeAsync(int cards, string? designName, CancellationToken ct = default,
        string feature = InfoID.Application.Services.LicensedFeatures.Printing)
    {
        cards = Math.Max(0, cards);

        // 1) An activated license on this PC: its own rules, no watermark, no trial limit.
        if (await IsLicenseValidAsync())
        {
            var counts = InfoID.Application.Services.LicensedFeatures.CountsCards(feature);
            var problem = await CheckLicenseAsync(feature, cards);
            return problem is null
                ? new PrintAuthorization(true, false, cards, PrintDenial.None, null) { CountsTowardCardLimit = counts }
                : new PrintAuthorization(false, false, cards, PrintDenial.LicenseProblem, problem) { CountsTowardCardLimit = counts };
        }

        // 2) Not activated and not signed in: log in to start the trial.
        if (!_session.IsSignedIn || _session.Token is not { } token)
            return new PrintAuthorization(false, false, cards, PrintDenial.SignInRequired, SignInMessage(_state.CardLimit));

        // 3) Trial: the server reserves the cards or refuses.
        if (!_portal.IsConfigured)
            return new PrintAuthorization(false, true, cards, PrintDenial.ServerUnavailable,
                "Trial printing isn't available in this version of InfoID. Activate InfoID to print.");
        if (cards == 0)
            return new PrintAuthorization(false, true, 0, PrintDenial.TrialNotEnough, "Nothing to print.");

        await FlushPendingCommitsAsync(token, ct);
        try
        {
            var response = await _portal.ReserveTrialCardsAsync(token, new TrialReserveRequest
            {
                Cards = cards,
                DesignName = designName,
                MachineFingerprint = TryFingerprint(),
                AppVersion = CustomerPortalClient.AppVersion,
            }, ct);
            _state.Apply(response);

            if (response.Success && response.ReservationId is { } id)
            {
                Log.Info($"Trial print authorized: {cards} card(s), {response.CardsRemaining} left after this job");
                return new PrintAuthorization(true, true, cards, PrintDenial.None, null, id);
            }

            var denial = response.CardsRemaining <= 0 ? PrintDenial.TrialUsedUp : PrintDenial.TrialNotEnough;
            return new PrintAuthorization(false, true, cards, denial,
                response.Message ?? (denial == PrintDenial.TrialUsedUp
                    ? $"You've used all {response.CardLimit} trial cards. Activate InfoID to keep printing."
                    : $"Only {response.CardsRemaining} trial cards left. Print fewer cards, or activate InfoID."));
        }
        catch (SessionExpiredException)
        {
            _session.SignOut();
            return new PrintAuthorization(false, false, cards, PrintDenial.SignInRequired,
                "Your session has expired. Log in again to keep printing trial cards.");
        }
        catch (LicenseServerUnavailableException ex)
        {
            Log.Warn("Trial print refused: server unreachable", ex);
            return new PrintAuthorization(false, true, cards, PrintDenial.ServerUnavailable,
                "Trial printing needs an internet connection, so InfoID can count your trial cards. " + ex.Message);
        }
    }

    /// <summary>The job was sent to the printer: count the cards. Never throws.</summary>
    public async Task CompleteAsync(PrintAuthorization authorization, int cardsPrinted)
    {
        if (!authorization.IsAllowed || authorization.Finished) return;
        authorization.Finished = true;
        cardsPrinted = Math.Max(0, cardsPrinted);

        if (!authorization.IsTrial)
        {
            // Licensed: count what really printed (more copies may have been chosen in
            // the Windows print dialog). Trial: never more than was reserved.
            // Visitor badges don't use up the card allowance.
            if (authorization.CountsTowardCardLimit) await CountLicensedCardsAsync(cardsPrinted);
            return;
        }

        cardsPrinted = Math.Min(cardsPrinted, authorization.Cards);
        var id = authorization.ReservationId!.Value;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                if (_session.Token is not { } token) break;
                var status = await _portal.CommitTrialCardsAsync(token, id, cardsPrinted);
                _state.Apply(status);
                InfoID.Desktop.Core.Services.Audit.Record(InfoID.Domain.Common.Interfaces.AuditModules.License, "TrialCardsPrinted",
                    $"{cardsPrinted} trial card{(cardsPrinted == 1 ? "" : "s")} printed ({status.CardsUsed} of {status.CardLimit} trial cards used)",
                    after: new { Cards = cardsPrinted, status.CardsUsed, status.CardLimit, status.CardsRemaining });
                return;
            }
            catch (LicenseServerUnavailableException ex)
            {
                Log.Warn($"Trial commit attempt {attempt} failed", ex);
                if (attempt < 3) await Task.Delay(TimeSpan.FromSeconds(attempt * 2));
            }
            catch (Exception ex)
            {
                // Session expired or an unexpected reply: keep it for the next attempt.
                Log.Warn("Trial commit failed", ex);
                break;
            }
        }

        // Couldn't reach the server: remember it and report on the next print or start.
        // The server counts a late report even after the reservation has expired.
        await SavePendingCommitAsync(new PendingCommit(id, cardsPrinted, _session.Profile?.Email));
    }

    /// <summary>The job failed or was cancelled: give the trial cards back. Never throws.</summary>
    public async Task CancelAsync(PrintAuthorization authorization)
    {
        if (!authorization.IsAllowed || authorization.Finished) return;
        authorization.Finished = true;
        if (!authorization.IsTrial || _session.Token is not { } token) return;
        try
        {
            var status = await _portal.ReleaseTrialCardsAsync(token, authorization.ReservationId!.Value);
            _state.Apply(status);
        }
        catch (Exception ex)
        {
            // The reservation expires on the server on its own (and the cards come back).
            Log.Warn("Trial release failed; the reservation will expire on the server", ex);
        }
    }

    /// <summary>Reports trial jobs that printed while the server couldn't be reached.</summary>
    public async Task FlushPendingCommitsAsync(string token, CancellationToken ct = default)
    {
        if (!File.Exists(_pendingPath)) return;
        await PendingGate.WaitAsync(ct);
        try
        {
            var pending = ReadPending();
            if (pending.Count == 0) return;
            var email = _session.Profile?.Email;
            var remaining = new List<PendingCommit>();
            foreach (var item in pending)
            {
                // Only the account that printed can report it.
                if (!string.Equals(item.AccountEmail, email, StringComparison.OrdinalIgnoreCase))
                {
                    remaining.Add(item);
                    continue;
                }
                try
                {
                    var status = await _portal.CommitTrialCardsAsync(token, item.ReservationId, item.CardsPrinted, ct);
                    _state.Apply(status);
                    Log.Info($"Reported {item.CardsPrinted} trial card(s) printed while offline");
                }
                catch (LicenseServerUnavailableException)
                {
                    remaining.Add(item);
                }
            }
            WritePending(remaining);
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't report pending trial cards", ex);
        }
        finally
        {
            PendingGate.Release();
        }
    }

    // ------------------------------------------------------------------ helpers ----

    private sealed record PendingCommit(Guid ReservationId, int CardsPrinted, string? AccountEmail);

    private async Task SavePendingCommitAsync(PendingCommit commit)
    {
        await PendingGate.WaitAsync();
        try
        {
            var pending = ReadPending();
            pending.Add(commit);
            WritePending(pending);
            Log.Warn($"Trial print of {commit.CardsPrinted} card(s) couldn't be reported; it will be sent on the next print");
        }
        finally
        {
            PendingGate.Release();
        }
    }

    private List<PendingCommit> ReadPending()
    {
        try
        {
            return File.Exists(_pendingPath)
                ? JsonSerializer.Deserialize<List<PendingCommit>>(File.ReadAllText(_pendingPath)) ?? new List<PendingCommit>()
                : new List<PendingCommit>();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new List<PendingCommit>();
        }
    }

    private void WritePending(List<PendingCommit> pending)
    {
        try
        {
            if (pending.Count == 0)
            {
                if (File.Exists(_pendingPath)) File.Delete(_pendingPath);
                return;
            }
            File.WriteAllText(_pendingPath, JsonSerializer.Serialize(pending));
        }
        catch (IOException ex)
        {
            Log.Warn("Couldn't save pending trial cards", ex);
        }
    }

    private async Task<bool> IsLicenseValidAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<ILicenseService>().GetCurrentStatusAsync()).IsValid;
        }
        catch (Exception ex)
        {
            Log.Warn("License status couldn't be read; treating as not activated", ex);
            return false;
        }
    }

    /// <summary>Null when the activated license allows printing this many cards.</summary>
    private async Task<string?> CheckLicenseAsync(string feature, int cards)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var check = await scope.ServiceProvider.GetRequiredService<ILicenseService>()
                .CheckFeatureAllowedAsync(feature, cards);
            return check.IsAllowed ? null : check.Reason;
        }
        catch (Exception ex)
        {
            return "The license could not be checked: " + ex.GetBaseException().Message;
        }
    }

    private async Task CountLicensedCardsAsync(int cards)
    {
        if (cards <= 0) return;
        try
        {
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ILicenseService>().IncrementCardsIssuedAsync(cards);
        }
        catch (Exception ex)
        {
            // The cards are printed; a counting problem isn't a print failure.
            Log.Warn("License card count failed", ex);
        }
    }

    private string? TryFingerprint()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            return scope.ServiceProvider.GetService<IMachineFingerprintProvider>()?.GetFingerprint().Hex;
        }
        catch
        {
            return null;
        }
    }
}
