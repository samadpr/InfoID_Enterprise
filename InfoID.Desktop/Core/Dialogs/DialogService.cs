using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace InfoID.Desktop.Core.Dialogs;

/// <summary>
/// Default <see cref="IDialogService"/> implementation. Hosts the resolved View for a
/// dialog ViewModel inside a lightweight, chrome-consistent <see cref="Window"/> and
/// completes the returned Task when the ViewModel raises CloseRequested.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly ViewLocator _viewLocator = new();

    public Task<TResult?> ShowDialogAsync<TViewModel, TResult>(TViewModel viewModel)
        where TViewModel : DialogViewModelBase<TResult>
    {
        var tcs = new TaskCompletionSource<TResult?>();

        var content = _viewLocator.Build(viewModel);

        var owner = GetOwnerWindow();

        var window = new Window
        {
            Content = content,
            CanResize = viewModel.CanResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner is not null
                ? WindowStartupLocation.CenterOwner
                : WindowStartupLocation.CenterScreen,
            Title = viewModel.Title,
        };
        window.Classes.Add("dialogWindow");

        // Sizing. A dialog that asks for a PreferredSize gets exactly that size, with
        // SizeToContent switched OFF. Bug fix: SizeToContent.WidthAndHeight used to be
        // set together with Width/Height, and Avalonia then sized the window to the
        // content's measured size (a ScrollViewer/TabControl measures small), which
        // made the Print dialog open undersized. The requested size is clamped to the
        // screen's working area so a dialog never opens larger than the display, and a
        // minimum keeps it usable if the user resizes it down.
        if (viewModel.PreferredSize is { } size)
        {
            var (width, height) = ClampToOwner(size.Width, size.Height, owner);

            window.SizeToContent = SizeToContent.Manual;
            window.Width = width;
            window.Height = height;
            window.MinWidth = Math.Min(width, Math.Max(480, width * 0.6));
            window.MinHeight = Math.Min(height, Math.Max(360, height * 0.6));
        }
        else
        {
            // Small message-style dialogs keep their existing fit-to-content behavior.
            window.SizeToContent = SizeToContent.WidthAndHeight;
        }

        void OnCloseRequested(TResult? result)
        {
            viewModel.CloseRequested -= OnCloseRequested;
            tcs.TrySetResult(result);
            window.Close();
        }

        viewModel.CloseRequested += OnCloseRequested;

        window.Closed += (_, _) => tcs.TrySetResult(default);

        if (owner is not null)
        {
            _ = window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }

        return tcs.Task;
    }

    /// <summary>Keeps a dialog within the working area of the screen the main window
    /// is on (so it is never bigger than the display, whatever size the main window
    /// currently is). Falls back to the unclamped size if screen info isn't available.</summary>
    private static (double Width, double Height) ClampToOwner(double width, double height, Window? owner)
    {
        try
        {
            var screens = owner?.Screens;
            var screen = owner is null ? null : screens?.ScreenFromTopLevel(owner) ?? screens?.Primary;
            if (screen is null)
                return (width, height);

            var scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;
            var availableWidth = screen.WorkingArea.Width / scaling;
            var availableHeight = screen.WorkingArea.Height / scaling;

            return (Math.Min(width, availableWidth * 0.94), Math.Min(height, availableHeight * 0.92));
        }
        catch (Exception)
        {
            return (width, height);
        }
    }

    private static Window? GetOwnerWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }
}
