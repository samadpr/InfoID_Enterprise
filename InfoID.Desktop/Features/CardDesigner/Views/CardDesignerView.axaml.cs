using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.CardDesigner.Views.Controls;

namespace InfoID.Desktop.Features.CardDesigner.Views;

public partial class CardDesignerView : UserControl
{
    public CardDesignerView()
    {
        InitializeComponent();

        var canvas = this.FindControl<CardCanvasView>("CardCanvas");
        var editBox = this.FindControl<TextBox>("InlineEditBox");
        var hRuler = this.FindControl<RulerView>("HorizontalRuler");
        var vRuler = this.FindControl<RulerView>("VerticalRuler");

        if (canvas is not null && editBox is not null)
        {
            canvas.TextEditRequested += (_, element) => BeginInlineEdit(element, canvas, editBox);
            editBox.KeyDown += (_, e) => OnEditBoxKeyDown(e);
            editBox.LostFocus += (_, _) => CommitInlineEdit();
        }

        // Rulers (Part 22): feed the cursor-position marker from the canvas's own
        // pointer-move, and forward drag-out-a-guide gestures (Part 21/23) from each
        // ruler into the active tab's guide collections.
        if (canvas is not null)
        {
            canvas.PointerMoved += (_, e) =>
            {
                var point = e.GetPosition(canvas);
                var mm = canvas.ScreenPointToFocusedMm(point);
                hRuler?.UpdateCursorPosition(mm?.X);
                vRuler?.UpdateCursorPosition(mm?.Y);
            };
            canvas.PointerExited += (_, _) =>
            {
                hRuler?.UpdateCursorPosition(null);
                vRuler?.UpdateCursorPosition(null);
            };
        }

        if (hRuler is not null)
        {
            hRuler.GuidePreviewChanged += (_, mm) => SetGuidePreview(mm, GuideOrientation.Vertical);
            hRuler.GuideCommitted += (_, mm) => { ViewModel?.ActiveTab?.AddVerticalGuide(mm); canvas?.InvalidateVisual(); };
        }
        if (vRuler is not null)
        {
            vRuler.GuidePreviewChanged += (_, mm) => SetGuidePreview(mm, GuideOrientation.Horizontal);
            vRuler.GuideCommitted += (_, mm) => { ViewModel?.ActiveTab?.AddHorizontalGuide(mm); canvas?.InvalidateVisual(); };
        }
    }

    private void SetGuidePreview(double? mm, GuideOrientation orientation)
    {
        var tab = ViewModel?.ActiveTab;
        if (tab is null) return;
        tab.GuidePreviewOrientation = orientation;
        tab.GuidePreviewMm = mm;
    }

    private CardDesignerViewModel? ViewModel => DataContext as CardDesignerViewModel;

    /// <summary>Positions the overlay TextBox exactly over the double-clicked element
    /// (via CardCanvasView.GetElementScreenRect, so it can never drift from what's
    /// drawn), then focuses it with the existing text selected -- standard "double-click
    /// to edit" behaviour (Part 3/80).</summary>
    private void BeginInlineEdit(TextElement element, CardCanvasView canvas, TextBox editBox)
    {
        ViewModel?.ActiveTab?.BeginInlineEdit(element);

        var rect = canvas.GetElementScreenRect(element);
        if (rect is { } r)
        {
            editBox.Margin = new Thickness(r.X, r.Y, 0, 0);
            editBox.Width = System.Math.Max(r.Width, 20);
            editBox.Height = System.Math.Max(r.Height, 18);
            editBox.FontSize = element.FontSize;
        }

        // Focus needs to happen after the TextBox has actually become visible/measured,
        // which only occurs once the DataContext/IsVisible bindings above have run --
        // deferring one dispatcher tick is the simplest reliable way to do that.
        Dispatcher.UIThread.Post(() =>
        {
            editBox.Focus();
            editBox.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void OnEditBoxKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitInlineEdit();
                e.Handled = true;
                break;
            case Key.Escape:
                ViewModel?.ActiveTab?.CancelInlineEdit();
                e.Handled = true;
                break;
        }
    }

    private void CommitInlineEdit() => ViewModel?.ActiveTab?.CommitInlineEdit();
}
