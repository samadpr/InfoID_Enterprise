using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.Account.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Account.Views;

/// <summary>
/// Account &amp; license as a dialog over the main window (opened at start-up when this
/// computer isn't activated, and from the header account button). Uses the same view
/// and view model as the page. When that page sends the user somewhere (Start designing,
/// See editions, Activate offline, Home) the main window shows it and this dialog closes.
/// </summary>
public partial class AccountDialogWindow : Window
{
    private readonly INavigationService? _navigation;
    private readonly AccountLicenseViewModel? _page;

    /// <summary>For the XAML previewer only.</summary>
    public AccountDialogWindow() => InitializeComponent();

    public AccountDialogWindow(INavigationService navigation, AccountLicenseViewModel page) : this()
    {
        _navigation = navigation;
        _page = page;

        _page.OnNavigatedTo(null);              // same start-up the page gets in the main window
        PageHost.Content = _page;
        _navigation.CurrentViewModelChanged += OnMainPageChanged;
    }

    /// <summary>Opens the dialog over <paramref name="owner"/> and waits until it closes.</summary>
    public static Task ShowAsync(Window owner)
    {
        var services = InfoID.Desktop.App.Services;
        var dialog = new AccountDialogWindow(
            services.GetRequiredService<INavigationService>(),
            services.GetRequiredService<AccountLicenseViewModel>());
        return dialog.ShowDialog(owner);
    }

    private void OnMainPageChanged(object? sender, EventArgs e) => Close();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (_navigation is not null) _navigation.CurrentViewModelChanged -= OnMainPageChanged;
        _page?.OnNavigatedFrom();
        base.OnClosed(e);
    }
}
