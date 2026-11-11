using Avalonia;
using Avalonia.Controls;
using InfoID.Desktop.Features.Rfid.ViewModels;

namespace InfoID.Desktop.Features.Rfid.Views;

public partial class NfcWriteDialogView : UserControl
{
    public NfcWriteDialogView() => InitializeComponent();

    /// <summary>The window may be closed with the title-bar X: stop listening to the reader.</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        (DataContext as NfcWriteDialogViewModel)?.OnViewClosed();
    }
}
