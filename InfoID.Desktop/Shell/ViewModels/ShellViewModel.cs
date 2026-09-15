using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Core.Services;
using InfoID.Desktop.Features.BlankCard.ViewModels;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.Templates.ViewModels;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using System;
using System.Threading.Tasks;

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
    private readonly IDialogService _dialogService;

    /// <summary>The one, singleton Card Designer instance (see App.axaml.cs) --
    /// injected directly rather than resolved through navigation, so its Tabs are
    /// reachable (for the header's "Windows" menu) even while some other page is
    /// currently shown. Unlike ActiveDesigner below, this is never null.</summary>
    public CardDesignerViewModel Designer { get; }

    public ShellViewModel(INavigationService navigationService, IThemeService themeService, IDialogService dialogService, CardDesignerViewModel designer)
    {
        _navigationService = navigationService;
        _themeService = themeService;
        _dialogService = dialogService;
        Designer = designer;

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

    /// <summary>Non-null only while the Card Designer is the active page. The header's
    /// File/Edit/View/Windows menus bind through this so designer-specific commands
    /// (Save, Undo, Zoom, ...) are only visible/enabled when there's actually a designer
    /// to act on, instead of the previous always-on-but-does-nothing menu (Part 5/6/22).</summary>
    public CardDesignerViewModel? ActiveDesigner => CurrentPage as CardDesignerViewModel;

    partial void OnCurrentPageChanged(ViewModelBase? value) => OnPropertyChanged(nameof(ActiveDesigner));

    /// <summary>Windows menu: jumps to an already-open design tab from anywhere in the
    /// app (e.g. from Home), without disturbing any other open tabs. Sets the tab
    /// active directly on the persistent Designer instance, then navigates to it --
    /// navigating with no parameter is a safe no-op on an already-populated designer
    /// (see CardDesignerViewModel.OpenFromParameterAsync), so this never adds a
    /// spurious blank tab on top of the one being switched to.</summary>
    [RelayCommand]
    private void ActivateDesignTab(CardDesignTabViewModel tab)
    {
        Designer.ActiveTab = tab;

        // Only actually navigate if some other page is currently shown -- if the
        // designer is already the active page (just switching between its own open
        // tabs), setting ActiveTab above is all that's needed, and skipping
        // NavigateTo here avoids pushing a redundant same-instance back-stack entry.
        if (CurrentPage is not CardDesignerViewModel)
        {
            _navigationService.NavigateTo<CardDesignerViewModel>();
        }
    }

    public bool CanGoBack => _navigationService.CanGoBack;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack() => _navigationService.GoBack();

    [RelayCommand]
    private void ToggleTheme() => _themeService.Toggle();

    [RelayCommand]
    private void GoHome() => _navigationService.NavigateToRoot<WelcomeViewModel>();

    /// <summary>File > New Card: same entry point as the Home screen's own "New Card"
    /// action (Part 6) -- the format-picker flow, not a bare blank tab, so a user
    /// starting from the header menu gets the same experience either way.</summary>
    [RelayCommand]
    private void NewCard() => _navigationService.NavigateTo<BlankCardViewModel>();

    /// <summary>File > Open: the closest real equivalent to "open a design" this
    /// codebase has today is the Template gallery/list of saved designs (Part 6/62).
    /// There is no separate file-open dialog -- InfoID designs aren't loose files, they
    /// live in the local database.</summary>
    [RelayCommand]
    private void OpenTemplates() => _navigationService.NavigateTo<TemplatesViewModel>();

    /// <summary>File > Exit. Goes through the classic desktop lifetime's own Shutdown
    /// rather than Environment.Exit, so any registered OnExit/shutdown hooks still run
    /// normally instead of being skipped.</summary>
    [RelayCommand]
    private void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    /// <summary>Full Screen (Part 24) is a plain window-state toggle, not something
    /// specific to any one page, so the ViewModel can't reach the Window directly (Views
    /// own Windows, not ViewModels). ShellView.axaml.cs subscribes to this event once
    /// and flips its own WindowState -- same request/react shape already used elsewhere
    /// in this codebase (e.g. CardCanvasView.TextEditRequested).</summary>
    public event EventHandler? ToggleFullScreenRequested;

    [RelayCommand]
    private void ToggleFullScreen() => ToggleFullScreenRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private async Task ShowAbout() =>
        await _dialogService.ShowDialogAsync<AboutViewModel, bool>(new AboutViewModel());
}

