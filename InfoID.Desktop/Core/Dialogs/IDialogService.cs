using System;
using System.Threading.Tasks;

namespace InfoID.Desktop.Core.Dialogs;

/// <summary>
/// Centralized dialog abstraction. ViewModels ask for a dialog by ViewModel type only --
/// they never construct a Window. <see cref="DialogService"/> resolves the matching View
/// via the shared ViewLocator, hosts it in a themed dialog window and returns the typed
/// result once the user closes it.
/// </summary>
public interface IDialogService
{
    Task<TResult?> ShowDialogAsync<TViewModel, TResult>(TViewModel viewModel)
        where TViewModel : DialogViewModelBase<TResult>;
}
