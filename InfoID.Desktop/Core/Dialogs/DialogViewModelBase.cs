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

    /// <summary>The title DialogService puts in the hosted Window's title bar.
    /// Defaults to plain "InfoID" -- exactly the hardcoded value every dialog already
    /// used before this property existed, so every existing dialog's behavior is
    /// completely unchanged unless it explicitly overrides this. A dialog that wants
    /// its own title (e.g. "InfoID Image Editor") overrides this property; nothing
    /// else about DialogViewModelBase or DialogService's hosting behavior changes.</summary>
    public virtual string Title => "InfoID";

    /// <summary>Whether the hosted Window can be resized/maximized by the user.
    /// Defaults to false -- exactly DialogService's previous hardcoded behavior for
    /// every dialog -- so nothing changes for any dialog that doesn't opt in. A dialog
    /// with content that might not fit every screen (e.g. the Image Editor) overrides
    /// this to true, so a user on a smaller display can resize/maximize rather than
    /// have content (like the Cancel/Done buttons) pushed off-screen with no way to
    /// reach it.</summary>
    public virtual bool CanResize => false;

    /// <summary>Explicit initial Window size, or null to leave DialogService's Window
    /// at its own default sizing (exactly the previous behavior for every dialog that
    /// doesn't override this). Added after a real, reported bug: DialogService's
    /// Window never had SizingToContent enabled and never had an explicit Width/
    /// Height set, so it was falling back to Avalonia's own default Window size --
    /// which is smaller than a content-heavy dialog like the Image Editor actually
    /// needs. A Border's own MinWidth/MinHeight in a View only constrains layout
    /// WITHIN whatever space the Window already has; it cannot make an undersized
    /// Window grow to fit, which is exactly what was causing the Image Editor's
    /// heading to render cramped against the title bar and its footer buttons to sit
    /// squeezed at the very bottom edge. A dialog with content that needs real room
    /// (again, the Image Editor) overrides this with a generous explicit size instead.</summary>
    public virtual (double Width, double Height)? PreferredSize => null;

    protected void RequestClose(TResult? result) => CloseRequested?.Invoke(result);
}
