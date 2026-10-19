using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using InfoID.AdminLicenseManager.ViewModels;

namespace InfoID.AdminLicenseManager.Views;

public partial class ActivationSimulatorView : UserControl
{
    public ActivationSimulatorView() => InitializeComponent();

    private async void OnCopyRequestCode(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ActivationSimulatorViewModel { RequestCode: { } code }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(code);
    }
}
