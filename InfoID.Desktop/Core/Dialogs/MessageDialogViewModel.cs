using CommunityToolkit.Mvvm.Input;

namespace InfoID.Desktop.Core.Dialogs;

/// <summary>
/// Generic message dialog (Part 68: "show useful user-friendly messages", "no silent
/// failures"). Reusable for any feature that needs to surface a message to the user
/// without inventing a one-off dialog each time -- e.g. .infoid import validation
/// failures, save errors, and (in confirmation mode) "are you sure?" prompts before a
/// destructive action like deleting a recent design.
/// </summary>
public sealed partial class MessageDialogViewModel : DialogViewModelBase<bool>
{
    // "new" here is deliberate, not a mistake: this Title is the dialog's own
    // in-content heading (bound by this dialog's View), a completely different thing
    // from DialogViewModelBase.Title (the hosting Window's title bar text, which
    // DialogService reads through the generic TViewModel constraint and therefore
    // always sees as the base class's plain "InfoID" default for this dialog type,
    // regardless of this property) -- explicit "new" documents that the name
    // collision is intentional and harmless rather than leaving the compiler's
    // default "hides inherited member" warning unexplained.
    public new string Title { get; }
    public string Message { get; }
    public bool IsError { get; }

    /// <summary>True shows Confirm/Cancel (result true/false); false shows a single OK
    /// (always closes with true, since there's nothing to confirm).</summary>
    public bool IsConfirmation { get; }

    public string ConfirmText { get; }
    public string CancelText { get; }

    public MessageDialogViewModel(string title, string message, bool isError = false)
    {
        Title = title;
        Message = message;
        IsError = isError;
        IsConfirmation = false;
        ConfirmText = "OK";
        CancelText = "Cancel";
    }

    private MessageDialogViewModel(string title, string message, string confirmText, string cancelText)
    {
        Title = title;
        Message = message;
        IsError = false;
        IsConfirmation = true;
        ConfirmText = confirmText;
        CancelText = cancelText;
    }

    /// <summary>Builds a Confirm/Cancel variant of this dialog -- a separate factory
    /// method rather than another constructor overload, so a plain
    /// "new MessageDialogViewModel(title, message)" info dialog can never accidentally
    /// end up in confirmation mode by a misplaced argument.</summary>
    public static MessageDialogViewModel Confirmation(string title, string message, string confirmText = "Delete", string cancelText = "Cancel") =>
        new(title, message, confirmText, cancelText);

    [RelayCommand]
    private void Close() => RequestClose(true);

    [RelayCommand]
    private void Cancel() => RequestClose(false);
}
