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
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = viewModel.CanResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            //SystemDecorations = SystemDecorations.BorderOnly,
            Title = viewModel.Title,
        };
        window.Classes.Add("dialogWindow");

        // Bug fix: this Window never had SizingToContent enabled and never had an
        // explicit size, so it fell back to Avalonia's own small default -- a
        // content-heavy dialog's own Border.MinWidth/MinHeight can only constrain
        // layout WITHIN whatever space the Window already has, it cannot make an
        // undersized Window grow to fit. See DialogViewModelBase.PreferredSize's own
        // doc comment for the reported symptoms this caused.
        if (viewModel.PreferredSize is { } size)
        {
            window.Width = size.Width;
            window.Height = size.Height;
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
