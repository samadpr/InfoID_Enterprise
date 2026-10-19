using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Licensing.Server.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.AdminLicenseManager.ViewModels;

public sealed record NavItem(string Title, string Glyph, Type ViewModelType);

/// <summary>Signed-in layout: left navigation + current section.</summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<ShellViewModel>();

    private readonly IServiceProvider _services;
    private readonly AuthService _auth;

    public event Action? SignedOut;

    public ObservableCollection<NavItem> NavItems { get; } = new()
    {
        new("Dashboard", "▦", typeof(DashboardViewModel)),
        new("License Requests", "✉", typeof(LicenseRequestsViewModel)),
        new("Customers", "☺", typeof(CustomersViewModel)),
        new("Activation Codes", "⚿", typeof(LicenseCodesViewModel)),
        new("Generate Serial Key", "⎘", typeof(GenerateSerialKeyViewModel)),
        new("Plans", "☷", typeof(PlansViewModel)),
        new("Audit Log", "☰", typeof(AuditLogViewModel)),
    };

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private ViewModelBase? _currentSection;

    public string AdminName { get; }
    public string AdminRole { get; }

    public ShellViewModel(IServiceProvider services, AuthService auth, AdminContext admin)
    {
        _services = services;
        _auth = auth;
        AdminName = admin.CurrentAdmin?.FullName ?? "";
        AdminRole = admin.CurrentAdmin?.Role ?? "";

        // Test tool until InfoID.Desktop's activation screen exists: Super Admin only.
        if (admin.IsSuperAdmin)
            NavItems.Add(new NavItem("Test: Customer Simulator", "⚗", typeof(ActivationSimulatorViewModel)));

        SelectedNav = NavItems[0];
    }

    /// <summary>One page instance per menu item for the whole signed-in session, so
    /// switching pages keeps what was typed (e.g. going Simulator -> Generate Serial
    /// Key -> Simulator no longer clears the Simulator). Bug fix: a new page was
    /// created on every click before.</summary>
    private readonly Dictionary<Type, ViewModelBase> _pages = new();

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null) return;

        if (!_pages.TryGetValue(value.ViewModelType, out var vm))
        {
            vm = (ViewModelBase)_services.GetRequiredService(value.ViewModelType);
            _pages[value.ViewModelType] = vm;
        }

        CurrentSection = vm;

        // Lists (dashboard, codes, audit log) still refresh each time they're opened.
        if (vm is ILoadable loadable) _ = loadable.LoadAsync();
    }

    /// <summary>Opens the folder with InfoID.LicenseManager-yyyy-MM-dd.log files.</summary>
    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(AppLogging.LogsDirectory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppLogging.LogsDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't open the log folder", ex);
        }
    }

    [RelayCommand]
    private async Task SignOut()
    {
        await _auth.SignOutAsync();
        SignedOut?.Invoke();
    }
}

public interface ILoadable
{
    Task LoadAsync();
}
