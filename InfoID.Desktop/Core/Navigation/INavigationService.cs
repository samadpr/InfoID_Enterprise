using System;
using InfoID.Desktop.ViewModels.Base;

namespace InfoID.Desktop.Core.Navigation;

/// <summary>
/// Centralized navigation abstraction for the whole application shell.
/// No feature or window should ever create another Window/View directly to move
/// between workspaces (Welcome -> Blank Card -> Card Designer, etc.) -- everything
/// goes through here so the shell has one place to reason about the back stack,
/// breadcrumbs and future multi-document tabs.
/// </summary>
public interface INavigationService
{
    /// <summary>The ViewModel currently displayed in the shell's content area.</summary>
    ViewModelBase? CurrentViewModel { get; }

    /// <summary>True when there is a previous page to return to.</summary>
    bool CanGoBack { get; }

    /// <summary>Raised whenever <see cref="CurrentViewModel"/> changes.</summary>
    event EventHandler? CurrentViewModelChanged;

    /// <summary>Navigates to a freshly resolved instance of <typeparamref name="TViewModel"/>.</summary>
    void NavigateTo<TViewModel>(object? parameter = null) where TViewModel : ViewModelBase;

    /// <summary>Navigates back to the previous entry in the stack, if any.</summary>
    void GoBack();

    /// <summary>Clears the back stack and navigates to <typeparamref name="TViewModel"/> as a fresh root (used for "Home").</summary>
    void NavigateToRoot<TViewModel>(object? parameter = null) where TViewModel : ViewModelBase;
}
