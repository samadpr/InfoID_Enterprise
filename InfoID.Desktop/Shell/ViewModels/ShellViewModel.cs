using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Core.Services;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;

namespace InfoID.Desktop.Shell.ViewModels;

/// <summary>
/// Root ViewModel for the application window. Owns the header state (theme, back
/// navigation) and hosts whatever page the <see cref="INavigationService"/> currently
/// points at in its content area. This is the single always-alive ViewModel for the
/// app's lifetime; every page underneath it is created/disposed by navigation.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INavigationService _navigationService;
    private readonly IThemeService _themeService;

    public ShellViewModel(INavigationService navigationService, IThemeService themeService)
    {
        _navigationService = navigationService;
        _themeService = themeService;

        _navigationService.CurrentViewModelChanged += (_, _) =>
        {
            CurrentPage = _navigationService.CurrentViewModel;
            GoBackCommand.NotifyCanExecuteChanged();
        };

        _themeService.ThemeChanged += (_, theme) => IsDarkTheme = theme == AppTheme.Dark;
        IsDarkTheme = _themeService.CurrentTheme == AppTheme.Dark;

        _navigationService.NavigateToRoot<WelcomeViewModel>();
        CurrentPage = _navigationService.CurrentViewModel;
    }

    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private bool _isDarkTheme;

    public bool CanGoBack => _navigationService.CanGoBack;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => _navigationService.GoBack();

    [RelayCommand]
    private void ToggleTheme() => _themeService.Toggle();

    [RelayCommand]
    private void GoHome() => _navigationService.NavigateToRoot<WelcomeViewModel>();
}
