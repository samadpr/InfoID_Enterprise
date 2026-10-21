using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using InfoID.Application.Dtos.Licensing;
using InfoID.Application.Interfaces;
using InfoID.Domain.Common.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Licensing.Services;

/// <summary>
/// The license edition shown in the window title bar ("InfoID Professional",
/// "InfoID Enterprise Trial", "InfoID (Not activated)"...). One shared instance: the
/// shell sets it at startup and the License page updates it whenever it reloads -- so
/// activating, deactivating or "Check now" change the title immediately.
///
/// It also decides whether the license needs the user's attention (Oct 2026 redesign):
/// the header account icon's status dot and the Welcome page banner both read
/// <see cref="Attention"/> and the Banner* texts, so they always agree.
/// </summary>
public sealed partial class LicenseBadgeState : ObservableObject
{
    private readonly IServiceScopeFactory _scopes;

    public LicenseBadgeState(IServiceScopeFactory scopes) => _scopes = scopes;

    /// <summary>Hidden until the first status is known (no flicker at startup).</summary>
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _text = "";
    /// <summary>Not activated, invalid, expired or in grace: shown in the warning color.</summary>
    [ObservableProperty] private bool _isWarning;
    [ObservableProperty] private string? _toolTip;

    /// <summary>Window title (title bar, taskbar, Alt+Tab): "InfoID Professional",
    /// "InfoID Enterprise Trial", "InfoID (Not activated)". Plain "InfoID" until known.</summary>
    [ObservableProperty] private string _windowTitle = "InfoID";

    /// <summary>Header button to the Editions page: "Get a license" (not activated),
    /// "Upgrade" (Standard/Professional), "Editions" (Enterprise).</summary>
    [ObservableProperty] private string _editionsButtonText = "Editions";

    // ------------------------------------------------------------ attention ----

    /// <summary>Licenses expiring within this many days show the amber "Renew" banner.</summary>
    public const int ExpiringSoonDays = 30;

    /// <summary>The last status read (null until the start-up check finishes).</summary>
    [ObservableProperty] private LicenseStatusDto? _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsAttention), nameof(IsCritical), nameof(IsWarningAttention), nameof(ShowBanner))]
    private LicenseAttention _attention;

    /// <summary>Short status for the account icon tooltip: "Not activated", "InfoID Standard, active".</summary>
    [ObservableProperty] private string _statusSummary = "Checking license...";

    [ObservableProperty] private string _bannerTitle = "";
    [ObservableProperty] private string _bannerText = "";
    /// <summary>"Activate", "Renew" or "Details".</summary>
    [ObservableProperty] private string _bannerActionText = "Activate";

    /// <summary>The Welcome banner's close button: hidden until InfoID starts again
    /// (deliberately not saved -- the problem is still there next time).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBanner))]
    private bool _isBannerDismissed;

    public bool NeedsAttention => Attention != LicenseAttention.None;
    public bool IsCritical => Attention == LicenseAttention.Critical;
    public bool IsWarningAttention => Attention == LicenseAttention.Warning;
    public bool ShowBanner => NeedsAttention && !IsBannerDismissed;

    public async Task RefreshAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            Apply(await scope.ServiceProvider.GetRequiredService<ILicenseService>().GetCurrentStatusAsync());
        }
        catch
        {
            // The badge is informational; the License page shows real errors.
        }
    }

    public void Apply(LicenseStatusDto status)
    {
        IsVisible = true;
        Status = status;
        ApplyBadge(status);
        ApplyAttention(status);
    }

    private void ApplyAttention(LicenseStatusDto status)
    {
        var edition = status.Tier is { } t
            ? $"InfoID {t}{(status.Type == LicenseType.Trial ? " Trial" : "")}"
            : "InfoID";

        if (!status.HasLicense || status.Status == LicenseStatus.Deactivated && status.Tier is null)
        {
            // Never activated, or deactivated here (by the user, or revoked by the vendor --
            // both are stored as Deactivated, so the text doesn't guess which).
            var deactivated = status.HasLicense;
            SetAttention(LicenseAttention.Warning, "Not activated",
                deactivated ? "The license on this computer is no longer active" : "InfoID isn't activated on this computer",
                deactivated
                    ? (status.Problem ?? "It was deactivated on this computer.") + " Sign in to activate a license again."
                    : "You can design and explore. Sign in and activate a license to print.",
                "Activate");
            return;
        }

        if (!status.IsValid)
        {
            // Copied from another PC, damaged or tampered with.
            SetAttention(LicenseAttention.Critical, "License not valid", "The license on this computer isn't valid",
                (status.Problem ?? "It can't be verified on this computer.") + " Activate again to continue printing.",
                "Activate");
            return;
        }

        switch (status.Status)
        {
            case LicenseStatus.Deactivated:
                SetAttention(LicenseAttention.Warning, "Not activated", $"{edition} is no longer active on this computer",
                    (status.Problem ?? "It was deactivated on this computer.") + " Sign in to activate it again.", "Activate");
                return;
            case LicenseStatus.Expired:
                SetAttention(LicenseAttention.Critical, $"{edition}, expired", $"Your {edition} license has expired",
                    $"It expired on {FormatDate(status.ExpiryDate)}. Your designs and data stay available; printing needs a valid license.",
                    "Renew");
                return;
            case LicenseStatus.GracePeriod:
                SetAttention(LicenseAttention.Critical, $"{edition}, expired", $"Your {edition} license has expired",
                    $"It expired on {FormatDate(status.ExpiryDate)}. Printing stops in {status.GracePeriodDaysRemaining ?? 0} day(s) unless it's renewed.",
                    "Renew");
                return;
        }

        if (status.DaysUntilExpiry is { } days && days <= ExpiringSoonDays)
        {
            SetAttention(LicenseAttention.Warning, $"{edition}, {DaysLeft(days)}", $"Your {edition} license expires soon",
                $"It expires in {DaysLeft(days)} ({FormatDate(status.ExpiryDate)}). Renew to keep printing.", "Renew");
            return;
        }

        Attention = LicenseAttention.None;
        StatusSummary = $"{edition}, active";
        BannerTitle = BannerText = "";
    }

    private void SetAttention(LicenseAttention level, string summary, string title, string text, string action)
    {
        Attention = level;
        StatusSummary = summary;
        BannerTitle = title;
        BannerText = text;
        BannerActionText = action;
    }

    private static string DaysLeft(int days) => days <= 0 ? "less than a day" : days == 1 ? "1 day" : $"{days} days";

    /// <summary>"3 Aug 2027": unambiguous whatever the regional date format.</summary>
    public static string FormatDate(DateTime? utc) =>
        utc is { } d ? d.ToLocalTime().ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture) : "";

    private void ApplyBadge(LicenseStatusDto status)
    {
        if (!status.HasLicense || !status.IsValid || status.Tier is not { } tier)
        {
            Text = status.HasLicense && status.Status != LicenseStatus.Deactivated ? "License not valid" : "Not activated";
            IsWarning = true;
            WindowTitle = $"InfoID ({Text})";
            EditionsButtonText = "Get a license";
            ToolTip = (status.Problem ?? "InfoID is not activated on this computer.") + " Click to activate.";
            return;
        }

        var edition = status.Type == LicenseType.Trial ? $"{tier} Trial" : tier.ToString();
        var expiry = status.ExpiryDate is { } e ? $"valid until {e.ToLocalTime():d}" : "perpetual license";
        WindowTitle = $"InfoID {edition}";
        EditionsButtonText = tier == LicenseTier.Enterprise ? "Editions" : "Upgrade";

        switch (status.Status)
        {
            case LicenseStatus.GracePeriod:
                Text = $"{edition} · Expired";
                IsWarning = true;
                ToolTip = $"InfoID {edition}: expired, {status.GracePeriodDaysRemaining} grace day(s) left. Click to renew.";
                break;
            case LicenseStatus.Expired:
                Text = $"{edition} · Expired";
                IsWarning = true;
                ToolTip = $"InfoID {edition}: expired. Printing is locked. Click to renew.";
                break;
            default:
                Text = edition;
                IsWarning = false;
                ToolTip = $"InfoID {edition}, {expiry}. Click for license details.";
                break;
        }
    }
}

/// <summary>How urgently the license needs the user: None (fine), Warning (amber: not
/// activated, trial, expiring soon), Critical (red: expired, not valid).</summary>
public enum LicenseAttention
{
    None,
    Warning,
    Critical,
}
