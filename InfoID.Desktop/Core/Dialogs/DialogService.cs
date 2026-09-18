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

        var window = new Window
        {
            Content = content,
            CanResize = viewModel.CanResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = viewModel.Title,
        };
        window.Classes.Add("dialogWindow");

        // Bug fix: SizeToContent.WidthAndHeight was left on unconditionally alongside
        // the PreferredSize block below, so PreferredSize's Width/Height were dead --
        // WidthAndHeight re-measures the Window from its content on every layout pass
        // and simply overwrites whatever Width/Height had just been set. That produced
        // two different symptoms depending on the dialog's content: the Print dialog
        // visibly resized on every tab switch (Print/Preview/Advanced Print Operations
        // each measure to a different content size), and the Image Editor's Window grew
        // to match a wide source image's own intrinsic size during the infinite-space
        // measure pass -- pushing its fixed-width side panel off the right edge of the
        // screen entirely for a landscape image, which is the actual bug report this
        // was fixed for. A dialog that declares PreferredSize now gets a real fixed-size
        // Window (SizeToContent.Manual) instead of an auto-measuring one; MinWidth/
        // MinHeight match it too so a resizable dialog (CanResize) can be grown but
        // never shrunk back down to the point its own controls get clipped again. A
        // dialog with no PreferredSize keeps the previous auto-fit-to-content behavior.
        if (viewModel.PreferredSize is { } size)
        {
            window.SizeToContent = SizeToContent.Manual;
            window.Width = size.Width;
            window.Height = size.Height;
            window.MinWidth = size.Width;
            window.MinHeight = size.Height;
        }
        else
        {
            window.SizeToContent = SizeToContent.WidthAndHeight;
        }

        void OnCloseRequested(TResult? result)
        {
            viewModel.CloseRequested -= OnCloseRequested;
            tcs.TrySetResult(result);
            window.Close();
        }

        viewModel.CloseRequested += OnCloseRequested;

        var owner = GetOwnerWindow();
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

    private static Window? GetOwnerWindow()
    {
        return Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
    }
}
