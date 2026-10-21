using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using InfoID.Desktop.Features.Account.ViewModels;

namespace InfoID.Desktop.Features.Account.Views;

public partial class AccountLicenseView : UserControl
{
    /// <summary>Below this width the account and requests column moves under My licenses.</summary>
    private const double StackBelowWidth = 900;

    public AccountLicenseView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => UpdateColumns(e.NewSize.Width);
    }

    /// <summary>Two columns on a wide window, one column on a narrow one, so the license
    /// card never gets squeezed (half-screen windows, small laptops, high DPI scaling).</summary>
    private void UpdateColumns(double width)
    {
        if (ColumnsGrid is null || SideColumn is null) return;
        var narrow = width < StackBelowWidth;

        ColumnsGrid.ColumnDefinitions = narrow
            ? new ColumnDefinitions("*")
            : new ColumnDefinitions("2*,24,*");
        Grid.SetColumn(SideColumn, narrow ? 0 : 2);
        Grid.SetRow(SideColumn, narrow ? 1 : 0);
        SideColumn.Margin = narrow ? new Avalonia.Thickness(0, 24, 0, 0) : default;
    }

    /// <summary>My licenses "Copy": the clipboard needs the window (TopLevel), so it lives
    /// in the view. The full key is copied even while it is shown masked.</summary>
    private async void OnCopyKeyClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string code } || string.IsNullOrWhiteSpace(code)) return;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is null) return;
            await clipboard.SetTextAsync(code);
            (DataContext as AccountLicenseViewModel)?.NotifyKeyCopied();
        }
        catch (Exception)
        {
            // Clipboard busy (another app holding it): nothing to do, the key is still visible via Show.
        }
    }
}
