using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.AdminLicenseManager.ViewModels;

/// <summary>Switches the window between the sign-in page and the signed-in shell.
/// Sign-in is required every session (FR-ADM-1).</summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private readonly IServiceProvider _services;

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    public MainWindowViewModel(IServiceProvider services)
    {
        _services = services;
        ShowLogin();
    }

    private void ShowLogin()
    {
        var login = _services.GetRequiredService<LoginViewModel>();
        login.SignedIn += ShowShell;
        CurrentPage = login;
        _ = login.InitializeAsync();
    }

    private void ShowShell()
    {
        var shell = _services.GetRequiredService<ShellViewModel>();
        shell.SignedOut += ShowLogin;
        CurrentPage = shell;
    }
}
