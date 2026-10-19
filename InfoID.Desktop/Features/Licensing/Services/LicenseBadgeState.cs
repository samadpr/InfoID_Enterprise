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
