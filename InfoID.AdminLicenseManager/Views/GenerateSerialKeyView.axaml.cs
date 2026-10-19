using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using InfoID.AdminLicenseManager.ViewModels;

namespace InfoID.AdminLicenseManager.Views;

public partial class GenerateSerialKeyView : UserControl
{
    public GenerateSerialKeyView() => InitializeComponent();

    /// <summary>Clipboard access needs the window (TopLevel), so it lives in the view.</summary>
    private async void OnCopySerialKey(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not GenerateSerialKeyViewModel { SerialKey: { } key }) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null)
            await clipboard.SetTextAsync(key);
    }
}
