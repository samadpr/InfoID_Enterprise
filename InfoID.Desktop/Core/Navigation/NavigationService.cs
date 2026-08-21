using System;
using System.Collections.Generic;
using InfoID.Desktop.ViewModels.Base;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Core.Navigation;

/// <summary>
/// Default <see cref="INavigationService"/> implementation. Resolves ViewModels from the
/// application's <see cref="IServiceProvider"/> (registered as transient), so every
/// navigation gets a clean ViewModel instance and pages don't need to know how to
/// construct their own dependencies.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Stack<NavigationEntry> _backStack = new();

    public NavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public ViewModelBase? CurrentViewModel { get; private set; }

    public bool CanGoBack => _backStack.Count > 0;

    public event EventHandler? CurrentViewModelChanged;

    public void NavigateTo<TViewModel>(object? parameter = null) where TViewModel : ViewModelBase
    {
        if (CurrentViewModel is not null)
        {
            CurrentViewModel.OnNavigatedFrom();
            _backStack.Push(new NavigationEntry(CurrentViewModel));
        }

        var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
        SetCurrent(viewModel, parameter);
    }

    public void NavigateToRoot<TViewModel>(object? parameter = null) where TViewModel : ViewModelBase
    {
        _backStack.Clear();
        var viewModel = _serviceProvider.GetRequiredService<TViewModel>();
        SetCurrent(viewModel, parameter);
    }

    public void GoBack()
    {
        if (!CanGoBack)
        {
            return;
        }

        CurrentViewModel?.OnNavigatedFrom();
        var entry = _backStack.Pop();
        SetCurrent(entry.ViewModel, null, isBackNavigation: true);
    }

    private void SetCurrent(ViewModelBase viewModel, object? parameter, bool isBackNavigation = false)
    {
        CurrentViewModel = viewModel;
        if (!isBackNavigation)
        {
            viewModel.OnNavigatedTo(parameter);
        }
        CurrentViewModelChanged?.Invoke(this, EventArgs.Empty);
    }
}
