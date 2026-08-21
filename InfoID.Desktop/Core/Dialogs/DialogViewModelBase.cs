using System;
using InfoID.Desktop.ViewModels.Base;

namespace InfoID.Desktop.Core.Dialogs;

/// <summary>
/// Base class for ViewModels shown through <see cref="IDialogService"/>. A dialog
/// ViewModel signals it wants to close by calling <see cref="RequestClose"/> with the
/// result (e.g. the OK/Cancel outcome). Future dialogs (print settings, database
/// connection, import/export, settings, license, ...) all plug in the same way.
/// </summary>
/// <typeparam name="TResult">The type returned when the dialog closes.</typeparam>
public abstract class DialogViewModelBase<TResult> : ViewModelBase
{
    public event Action<TResult?>? CloseRequested;

    protected void RequestClose(TResult? result) => CloseRequested?.Invoke(result);
}
