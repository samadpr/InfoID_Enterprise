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
            //SizingToContent = SizingToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            //SystemDecorations = SystemDecorations.BorderOnly,
            Title = "InfoID",
        };
        window.Classes.Add("dialogWindow");

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
