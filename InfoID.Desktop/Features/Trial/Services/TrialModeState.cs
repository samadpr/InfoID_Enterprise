using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Features.Account.Services;
using InfoID.Desktop.Features.Licensing.Services;
using InfoID.Shared.Contracts.Accounts;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Trial.Services;

/// <summary>
/// Which printing mode InfoID is in, for the UI (watermark on screen, trial counter):
///   Activated  -- this PC has a valid license: no watermark, no trial limit.
///   Trial      -- signed in to an InfoID account, no valid license: trial watermark,
///                 limited trial cards (counted on the server).
///   Signed out -- no license, not signed in: design and preview freely, printing asks
///                 to log in.
/// This is display state only. Whether a print may actually happen is decided by
/// <see cref="PrintAuthorizationService"/> at print time, with the server.
/// </summary>
public sealed partial class TrialModeState : ObservableObject
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<TrialModeState>();
    public const int DefaultCardLimit = 10;

    private readonly IServiceScopeFactory _scopes;
    private readonly AccountSession _session;
    private readonly CustomerPortalClient _portal;
    private int _refreshing;
    private bool _refreshAgain;

    /// <summary>For views that aren't created through DI (the design canvas, preview
    /// surfaces). Set once by the singleton.</summary>
    public static TrialModeState? Current { get; private set; }

    public TrialModeState(IServiceScopeFactory scopes, AccountSession session, CustomerPortalClient portal, LicenseBadgeState licenseBadge)
    {
        _scopes = scopes;
        _session = session;
        _portal = portal;
        Current = this;
        IsSignedIn = session.IsSignedIn;

        // Signing in/out, activating or deactivating changes the mode.
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AccountSession.IsSignedIn) or nameof(AccountSession.Profile)) _ = RefreshAsync();
        };
        licenseBadge.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LicenseBadgeState.WindowTitle)) _ = RefreshAsync();
        };
    }

    /// <summary>Raised whenever the mode or the trial counts change (on the UI thread).</summary>
    public event EventHandler? Changed;

    [ObservableProperty] private bool _isActivated;
    [ObservableProperty] private bool _isSignedIn;
    [ObservableProperty] private bool _isKnown;
    [ObservableProperty] private bool _hasTrialStatus;
    [ObservableProperty] private int _cardLimit = DefaultCardLimit;
    [ObservableProperty] private int _cardsUsed;
    [ObservableProperty] private int _cardsReserved;
    [ObservableProperty] private int _cardsRemaining = DefaultCardLimit;
    [ObservableProperty] private string? _trialError;

    /// <summary>Signed in without a valid license: trial watermark and limit apply.</summary>
    public bool IsTrialMode => IsSignedIn && !IsActivated;

    /// <summary>Not activated and not signed in: printing needs a login first.</summary>
    public bool NeedsSignIn => !IsActivated && !IsSignedIn;

    /// <summary>The trial is used up (all cards printed).</summary>
    public bool TrialUsedUp => IsTrialMode && HasTrialStatus && CardsRemaining <= 0 && CardsReserved == 0;

    /// <summary>"3 of 10 trial cards used".</summary>
    public string TrialUsageText => HasTrialStatus
        ? $"{CardsUsed} of {CardLimit} trial card{(CardLimit == 1 ? "" : "s")} used"
        : "Trial mode";

    /// <summary>"7 left" / "none left".</summary>
    public string TrialRemainingText => !HasTrialStatus ? "" : CardsRemaining <= 0 ? "none left" : $"{CardsRemaining} left";

    /// <summary>Short header text, e.g. "Trial · 3/10 cards".</summary>
    public string HeaderText => HasTrialStatus ? $"Trial · {CardsUsed}/{CardLimit} cards" : "Trial";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsActivated) or nameof(IsSignedIn) or nameof(HasTrialStatus) or nameof(CardLimit)
            or nameof(CardsUsed) or nameof(CardsReserved) or nameof(CardsRemaining))
        {
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsTrialMode)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(NeedsSignIn)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(TrialUsedUp)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(TrialUsageText)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(TrialRemainingText)));
            base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(HeaderText)));
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Re-reads the license and, in trial mode, the trial counts from the server.
    /// Never throws; network problems leave the last known counts and set TrialError.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            _refreshAgain = true;
            return;
        }
        try
        {
            do
            {
                _refreshAgain = false;
                var activated = await IsLicenseValidAsync();
                var signedIn = _session.IsSignedIn;
                TrialStatusDto? trial = null;
                string? error = null;
                if (signedIn && !activated && _session.Token is { } token && _portal.IsConfigured)
                {
                    try
                    {
                        trial = await _portal.GetTrialStatusAsync(token, ct);
                    }
                    catch (SessionExpiredException)
                    {
                        // The account session ended on the server: sign out locally too.
                        await OnUiAsync(() => _session.SignOut());
                        signedIn = false;
                    }
                    catch (InfoID.Application.Interfaces.LicenseServerUnavailableException ex)
                    {
                        error = ex.Message;
                    }
                }

                await OnUiAsync(() =>
                {
                    IsActivated = activated;
                    IsSignedIn = signedIn;
                    TrialError = error;
                    if (trial is not null) Apply(trial);
                    else if (!signedIn || activated) HasTrialStatus = false;
                    IsKnown = true;
                });
            }
            while (_refreshAgain);
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't refresh the trial state", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    /// <summary>Takes the counts from any trial answer of the server.</summary>
    internal void Apply(TrialStatusDto status)
    {
        void Set()
        {
            CardLimit = status.CardLimit;
            CardsUsed = status.CardsUsed;
            CardsReserved = status.CardsReserved;
            CardsRemaining = status.CardsRemaining;
            HasTrialStatus = true;
        }
        if (Dispatcher.UIThread.CheckAccess()) Set();
        else Dispatcher.UIThread.Post(Set);
    }

    private async Task<bool> IsLicenseValidAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var status = await scope.ServiceProvider.GetRequiredService<ILicenseService>().GetCurrentStatusAsync();
            return status.IsValid;
        }
        catch (Exception ex)
        {
            Log.Warn("License status couldn't be read for the trial state", ex);
            return false;
        }
    }

    private static Task OnUiAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }
        return Dispatcher.UIThread.InvokeAsync(action).GetTask();
    }
}
