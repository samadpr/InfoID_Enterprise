using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.CardDesigner.Views.Controls;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Avalonia.Data;

namespace InfoID.Desktop.Features.CardDesigner.Views;

public partial class CardDesignerView : UserControl
{
    public CardDesignerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

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

    // ------------------------------------------------------------------ rename ----

    /// <summary>Double-click on a tab's title enters rename mode (Part 20), matching
    /// the canvas's own double-click-to-edit-text gesture. The Button and the rename
    /// TextBox live in the same Panel (see the tab strip DataTemplate) and swap
    /// visibility via IsRenaming, so once BeginRenameCommand flips that flag the
    /// TextBox becomes visible and just needs focus.</summary>
    private void OnTabTitleDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: CardDesignTabViewModel tab } control) return;

        tab.BeginRenameCommand.Execute(null);

        if (control.Parent is not Panel panel) return;
        var box = panel.Children.OfType<TextBox>().FirstOrDefault();
        if (box is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void OnTabRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: CardDesignTabViewModel tab }) return;

        switch (e.Key)
        {
            case Key.Enter:
                tab.CommitRenameCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                tab.CancelRenameCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Clicking away from the rename box commits rather than silently
    /// discarding the typed name -- only Escape should discard (Part 20/3).</summary>
    private void OnTabRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: CardDesignTabViewModel { IsRenaming: true } tab }) return;
        tab.CommitRenameCommand.Execute(null);
    }
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is CardDesignerViewModel viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;

            if (viewModel.ActiveTab is not null)
            {
                viewModel.ActiveTab.PropertyChanged += ActiveTab_PropertyChanged;
            }
        }
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CardDesignerViewModel.ActiveTab))
        {
            if (sender is CardDesignerViewModel viewModel &&
                viewModel.ActiveTab is not null)
            {
                viewModel.ActiveTab.PropertyChanged += ActiveTab_PropertyChanged;
            }
        }
    }

    private void ActiveTab_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CardDesignTabViewModel.DatabaseDataView))
        {
            BuildDatabaseGrid();
        }
    }

    private void BuildDatabaseGrid()
    {
        if (DatabaseDataGrid is null)
            return;

        DatabaseDataGrid.Columns.Clear();

        var tab = ViewModel?.ActiveTab;

        if (tab?.DatabaseDataView is null)
            return;

        var view = tab.DatabaseDataView;

        if (view.Table is null)
            return;

        foreach (DataColumn column in view.Table.Columns)
        {
            var columnName = column.ColumnName;

            var gridColumn = new DataGridTemplateColumn
            {
                Header = columnName,

                CellTemplate = new FuncDataTemplate<object>(
                    (item, _) =>
                    {
                        var value = "";

                        if (item is DataRowView row)
                        {
                            value = row[columnName] == DBNull.Value
                                ? ""
                                : row[columnName]?.ToString() ?? "";
                        }

                        return new TextBlock
                        {
                            Text = value,
                            Margin = new Thickness(6, 0),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };
                    },
                    supportsRecycling: true)
            };

            DatabaseDataGrid.Columns.Add(gridColumn);
        }
    }

    // ------------------------------------------------------------------ layers ----

    private static readonly DataFormat<LayerNode> LayerDragFormat =
        DataFormat.CreateInProcessFormat<LayerNode>("InfoID.LayerNode");

    /// <summary>Unified click-or-drag gesture for a layer row -- there's no separate
    /// drag-handle icon (there used to be one; it was a small, hover-only target that
    /// made dragging feel fiddly and made it hard to pull an element back out of a
    /// group). Any part of the row now works for both: press, then either release
    /// roughly where you pressed (selects) or move a few pixels first (drags). This
    /// mirrors how most layer panels behave and matches every Button already living
    /// inside the row (icon expand/collapse, lock/eye/delete) -- those mark their own
    /// PointerPressed handled via normal ButtonBase press handling, so a press that
    /// starts on one of them never reaches these three handlers at all.</summary>
    private LayerNode? _rowPressNode;
    private Control? _rowPressControl;
    private PointerPressedEventArgs? _rowPressArgs;
    private Point _rowPressPoint;
    private bool _rowDragStarted;

    private void OnLayerRowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: LayerNode node } control) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

        _rowPressNode = node;
        _rowPressControl = control;
        _rowPressArgs = e;
        _rowPressPoint = e.GetPosition(control);
        _rowDragStarted = false;
    }

    /// <summary>IsDragging drives the row's reduced-opacity "lifted" look for the
    /// duration of the drag (Border.layerRow.dragging in CardDesignerView.axaml) --
    /// purely visual feedback so a drag actually feels like it's carrying something,
    /// which is what made the very first version of this feature feel "not smooth."
    /// Avalonia 12's DragDrop surface (DataTransfer/DataTransferItem/DataFormat)
    /// replaced the older WPF-style DataObject API; no precedent for it existed
    /// anywhere else in this codebase before this feature.</summary>
    private async void OnLayerRowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_rowDragStarted || _rowPressNode is not { } node || _rowPressControl is not { } control || _rowPressArgs is not { } pressArgs) return;
        if (!ReferenceEquals(sender, control)) return;
        if (!e.GetCurrentPoint(control).Properties.IsLeftButtonPressed) return;

        var delta = e.GetPosition(control) - _rowPressPoint;
        if (Math.Abs(delta.X) < 4 && Math.Abs(delta.Y) < 4) return;

        _rowDragStarted = true;
        _rowPressNode = null;
        _rowPressControl = null;
        _rowPressArgs = null;

        var data = new DataTransfer();
        data.Add(DataTransferItem.Create(LayerDragFormat, node));

        node.IsDragging = true;
        try
        {
            await DragDrop.DoDragDropAsync(pressArgs, data, DragDropEffects.Move);
        }
        finally
        {
            node.IsDragging = false;
        }
    }

    /// <summary>If a drag never started, this was a plain click -- select (or toggle,
    /// with Ctrl/Shift) whatever the press landed on.</summary>
    private void OnLayerRowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasDragging = _rowDragStarted;
        var node = _rowPressNode;
        _rowPressNode = null;
        _rowPressControl = null;
        _rowPressArgs = null;
        _rowDragStarted = false;

        if (wasDragging || node is null) return;
        if (ViewModel?.ActiveTab is not { } tab) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            tab.ToggleLayerNodeSelectionCommand.Execute(node);
        }
        else
        {
            tab.SelectLayerNodeCommand.Execute(node);
        }
    }

    /// <summary>Toggles the drop-target highlight (Border.layerRow.dragOver) so hovering
    /// a row while dragging shows exactly where a drop would land, instead of the drag
    /// giving no feedback until you release.</summary>
    private void OnLayerRowDragEnter(object? sender, DragEventArgs e)
    {
        if (sender is Border { DataContext: LayerNode } border && e.DataTransfer.Contains(LayerDragFormat))
        {
            border.Classes.Add("dragOver");
        }
    }

    private void OnLayerRowDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is Border border) border.Classes.Remove("dragOver");
    }

    private void OnLayerRowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(LayerDragFormat) ? DragDropEffects.Move : DragDropEffects.None;
    }

    /// <summary>Hands the actual reorder/regroup decision to CardDesignTabViewModel.
    /// HandleLayerDrop -- this handler's only job is figuring out which two LayerNodes
    /// were involved. Marks the event handled so it doesn't also reach
    /// OnLayersPanelBackgroundDrop on the ScrollViewer this row lives inside.</summary>
    private void OnLayerRowDrop(object? sender, DragEventArgs e)
    {
        if (sender is Border border) border.Classes.Remove("dragOver");
        if (sender is not Control { DataContext: LayerNode target }) return;
        if (ViewModel?.ActiveTab is not { } tab) return;
        if (e.DataTransfer.TryGetValue(LayerDragFormat) is not LayerNode dragged) return;

        e.Handled = true;
        tab.HandleLayerDrop(dragged, target);
    }

    /// <summary>Dropping on blank panel area (not on any row) is the explicit "take this
    /// out of its group" gesture -- only reached when the drop didn't land on a row,
    /// since OnLayerRowDrop above marks the event handled.</summary>
    private void OnLayersPanelBackgroundDrop(object? sender, DragEventArgs e)
    {
        if (ViewModel?.ActiveTab is not { } tab) return;
        if (e.DataTransfer.TryGetValue(LayerDragFormat) is not LayerNode dragged) return;

        tab.HandleLayerDropToTopLevel(dragged);
    }

    // ------------------------------------------------------------ layer groups ----

    /// <summary>Double-click on a group folder's name enters rename mode -- same shape
    /// as OnTabTitleDoubleTapped above, just scoped to a LayerNode row instead of the
    /// tab strip.</summary>
    private void OnLayerGroupNameDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: LayerNode { IsGroup: true } node } control) return;
        if (ViewModel?.ActiveTab is not { } tab) return;

        tab.BeginRenameGroupCommand.Execute(node);

        if (control.Parent is not Panel panel) return;
        var box = panel.Children.OfType<TextBox>().FirstOrDefault();
        if (box is null) return;

        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private void OnLayerGroupRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: LayerNode node } || ViewModel?.ActiveTab is not { } tab) return;

        switch (e.Key)
        {
            case Key.Enter:
                tab.CommitRenameGroupCommand.Execute(node);
                e.Handled = true;
                break;
            case Key.Escape:
                tab.CancelRenameGroupCommand.Execute(node);
                e.Handled = true;
                break;
        }
    }

    private void OnLayerGroupRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox { DataContext: LayerNode { IsRenaming: true } node }) return;
        if (ViewModel?.ActiveTab is not { } tab) return;
        tab.CommitRenameGroupCommand.Execute(node);
    }
}
