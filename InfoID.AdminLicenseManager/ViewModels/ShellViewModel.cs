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
    private readonly IServiceProvider _services;
    private readonly AuthService _auth;

    public event Action? SignedOut;

    public ObservableCollection<NavItem> NavItems { get; } = new()
    {
        new("Dashboard", "▦", typeof(DashboardViewModel)),
        new("Activation Codes", "⚿", typeof(LicenseCodesViewModel)),
        new("Generate Serial Key", "⎘", typeof(GenerateSerialKeyViewModel)),
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
        SelectedNav = NavItems[0];
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null) return;
        var vm = (ViewModelBase)_services.GetRequiredService(value.ViewModelType);
        CurrentSection = vm;
        if (vm is ILoadable loadable) _ = loadable.LoadAsync();
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
