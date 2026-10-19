using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using InfoID.Desktop.Features.Licensing.ViewModels;

namespace InfoID.Desktop.Features.Licensing.Views;

public partial class LicenseActivationView : UserControl
{
    private LicenseActivationViewModel? _viewModel;

    public LicenseActivationView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel = DataContext as LicenseActivationViewModel;
            if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        };
    }

    /// <summary>When the offline section opens, scroll it into view -- it appears below
    /// the fold on smaller windows, which looked like "nothing happened".</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LicenseActivationViewModel.ShowOffline) && _viewModel?.ShowOffline == true)
            Dispatcher.UIThread.Post(() => OfflineCard.BringIntoView(), DispatcherPriority.Background);
    }

    /// <summary>Clipboard needs the window (TopLevel), so it lives in the view.</summary>
    private async void OnCopyRequestCode(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not LicenseActivationViewModel { RequestCode: { } code }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(code);
    }
}
