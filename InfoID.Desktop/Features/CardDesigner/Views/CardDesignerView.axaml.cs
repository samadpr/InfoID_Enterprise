using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.CardDesigner.Views.Controls;
using InfoID.Desktop.Features.CardDesigner.Views.Icons;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

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
        _headerCheckBox = this.FindControl<CheckBox>("SelectAllRowsCheckBox");

        if (DatabaseDataGrid is not null)
            DatabaseDataGrid.LostFocus += OnDatabaseGridLostFocus;

        if (_headerCheckBox is not null)
        {
            _headerCheckBox.IsCheckedChanged += (_, _) =>
            {
                if (_syncingHeaderCheck) return;
                ViewModel?.ActiveTab?.SetAllDatabaseRowsChecked(_headerCheckBox.IsChecked == true);
            };
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

    //private void ActiveTab_PropertyChanged(
    //    object? sender,
    //    System.ComponentModel.PropertyChangedEventArgs e)
    //{
    //    if (e.PropertyName == nameof(CardDesignTabViewModel.DatabaseDataView))
    //    {
    //        BuildDatabaseGrid();
    //    }
    //}
    private void ActiveTab_PropertyChanged(
    object? sender,
    System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CardDesignTabViewModel.DatabaseDataView))
        {
            BuildDatabaseGrid();
        }
        else if (e.PropertyName is nameof(CardDesignTabViewModel.DatabaseUnsavedCount)
                     or nameof(CardDesignTabViewModel.DatabaseFailedCount) &&
                 ReferenceEquals(sender, ViewModel?.ActiveTab))
        {
            Dispatcher.UIThread.Post(RefreshDatabaseGridCells, DispatcherPriority.Background);
        }
        else if (e.PropertyName == nameof(CardDesignTabViewModel.SelectedDatabaseRow) &&
                 ReferenceEquals(sender, ViewModel?.ActiveTab) &&
                 sender is CardDesignTabViewModel { SelectedDatabaseRow: { } row } &&
                 DatabaseDataGrid is not null)
        {
            // Record navigation (first/prev/next/last): bring the new current row on screen.
            Dispatcher.UIThread.Post(
                () => DatabaseDataGrid.ScrollIntoView(row, null),
                DispatcherPriority.Background);
        }
    }

    //private void BuildDatabaseGrid()
    //{
    //    if (DatabaseDataGrid is null)
    //        return;

    //    DatabaseDataGrid.Columns.Clear();

    //    var tab = ViewModel?.ActiveTab;

    //    if (tab?.DatabaseDataView is null)
    //        return;

    //    var view = tab.DatabaseDataView;

    //    if (view.Table is null)
    //        return;

    //    foreach (DataColumn column in view.Table.Columns)
    //    {
    //        var columnName = column.ColumnName;

    //        var gridColumn = new DataGridTemplateColumn
    //        {
    //            Header = columnName,

    //            CellTemplate = new FuncDataTemplate<object>(
    //    (item, _) =>
    //    {
    //        var value = "";

    //        if (item is DataRowView row)
    //        {
    //            value = row[columnName] == DBNull.Value
    //                ? ""
    //                : row[columnName]?.ToString() ?? "";
    //        }

    //        return new TextBlock
    //        {
    //            Text = value,
    //            Margin = new Thickness(6, 0),
    //            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
    //        };
    //    },
    //    supportsRecycling: true),

    //            CellEditingTemplate = new FuncDataTemplate<object>(
    //    (item, _) =>
    //    {
    //        var textBox = new TextBox
    //        {
    //            Margin = new Thickness(2),
    //            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
    //        };

    //        if (item is DataRowView row)
    //        {
    //            textBox.Text = row[columnName] == DBNull.Value
    //                ? ""
    //                : row[columnName]?.ToString() ?? "";

    //            textBox.LostFocus += (_, _) =>
    //            {
    //                row[columnName] =
    //                    string.IsNullOrWhiteSpace(textBox.Text)
    //                        ? DBNull.Value
    //                        : textBox.Text;
    //            };
    //        }

    //        return textBox;
    //    },
    //    supportsRecycling: true)
    //        };

    //        DatabaseDataGrid.Columns.Add(gridColumn);
    //    }
    //}


    private readonly List<Action> _rowCheckSyncs = new();
    private CheckBox? _headerCheckBox;
    private bool _syncingHeaderCheck;
    private CardDesignTabViewModel? _checkEventsTab;

    private void BuildDatabaseGrid()
    {
        if (DatabaseDataGrid is null)
            return;

        DatabaseDataGrid.Columns.Clear();
        _rowCheckSyncs.Clear();
        

        var tab = ViewModel?.ActiveTab;

        // Listen for tick changes on the current tab only.
        if (_checkEventsTab is not null)
            _checkEventsTab.DatabaseRowChecksChanged -= OnDatabaseRowChecksChanged;

        _checkEventsTab = tab;

        if (tab is not null)
            tab.DatabaseRowChecksChanged += OnDatabaseRowChecksChanged;

        // Rows saved (new IDs, cleared "unsaved" marks): refresh the visible cells.
        if (_saveEventsTab is not null)
            _saveEventsTab.DatabaseRowsSaved -= OnDatabaseRowsSaved;
        _saveEventsTab = tab;
        if (tab is not null)
            tab.DatabaseRowsSaved += OnDatabaseRowsSaved;

        if (tab?.DatabaseDataView is null)
            return;

        var view = tab.DatabaseDataView;

        if (view.Table is null)
            return;

        DatabaseDataGrid.Columns.Add(CreateRowCheckColumn(tab));
        OnDatabaseRowChecksChanged(tab, EventArgs.Empty);
        foreach (DataColumn column in view.Table.Columns)
        {
            var columnName = column.ColumnName;

            if (tab.IsDatabaseImageColumn(column))
            {
                DatabaseDataGrid.Columns.Add(CreateImageColumn(tab, columnName));
                continue;
            }

            var isKeyColumn = tab.IsDatabaseKeyColumn(column);

            DatabaseDataGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = columnName,
                // Internal Database: the key is given by the database, never typed.
                IsReadOnly = isKeyColumn,

                // The text follows the cell's CURRENT row (recycled cells must not show
                // another row's value). Tagged so saved rows can be refreshed in place.
                CellTemplate = new FuncDataTemplate<object>(
                    (item, _) =>
                    {
                        var textBlock = new TextBlock
                        {
                            Text = tab.GetDatabaseCellText(item as DataRowView, columnName),
                            Margin = new Thickness(6, 0),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                            Tag = new DatabaseCellTag(columnName),
                        };

                        if (isKeyColumn)
                            textBlock.Classes.Add("caption");

                        textBlock.DataContextChanged += (_, _) =>
                            textBlock.Text = tab.GetDatabaseCellText(textBlock.DataContext as DataRowView, columnName);

                        return textBlock;
                    },
                    supportsRecycling: false),

                // Typed values go through the view model, which converts them to the
                // field's type (number, date, yes/no) and marks the row for saving.
                // Valid text is written as you type, so nothing is lost if the next
                // click goes straight to Print or another row; a value that doesn't
                // fit is shown on the cell when you leave it.
                CellEditingTemplate = new FuncDataTemplate<object>(
                    (item, _) =>
                    {
                        var editRow = item as DataRowView;
                        var loading = true;

                        var textBox = new TextBox
                        {
                            Text = tab.GetDatabaseCellText(editRow, columnName),
                            Margin = new Thickness(2),
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
                        };
                        Avalonia.Automation.AutomationProperties.SetName(textBox, columnName);

                        void Apply(bool showError)
                        {
                            if (loading || editRow is null || editRow.Row.RowState == DataRowState.Detached)
                                return;

                            if (tab.TrySetDatabaseCell(editRow, columnName, textBox.Text, out var error))
                            {
                                textBox.Classes.Remove("cellError");
                                ToolTip.SetTip(textBox, null);
                            }
                            else if (showError)
                            {
                                textBox.Classes.Add("cellError");
                                ToolTip.SetTip(textBox, error);
                                tab.ReportDatabaseCellError(columnName, error);
                            }
                        }

                        textBox.DataContextChanged += (_, _) =>
                        {
                            if (textBox.DataContext is DataRowView row && !ReferenceEquals(row, editRow))
                            {
                                loading = true;
                                editRow = row;
                                textBox.Text = tab.GetDatabaseCellText(row, columnName);
                                loading = false;
                            }
                        };

                        textBox.TextChanged += (_, _) => Apply(showError: textBox.Classes.Contains("cellError"));
                        textBox.LostFocus += (_, _) => Apply(showError: true);

                        loading = false;
                        return textBox;
                    },
                    supportsRecycling: false)
            });
        }
    }

    private static string GetCellText(object? item, string columnName)
    {
        if (item is not DataRowView row ||
            row.Row.RowState == DataRowState.Detached ||
            !row.Row.Table.Columns.Contains(columnName))
        {
            return string.Empty;
        }

        var value = row[columnName];
        return value == DBNull.Value ? string.Empty : value?.ToString() ?? string.Empty;
    }

    /// <summary>First grid column: a tick box per row plus a select-all box in the header.
    /// Tick state lives in the ViewModel (not the DataTable), so it never reaches the
    /// database fields, saving, or card bindings.</summary>
    private DataGridTemplateColumn CreateRowCheckColumn(CardDesignTabViewModel tab)
    {
        return new DataGridTemplateColumn
        {
            Header = string.Empty,
            Width = new DataGridLength(60),
            CanUserResize = false,
            CanUserReorder = false,
            CanUserSort = false,

            CellTemplate = new FuncDataTemplate<object>(
                (_, _) =>
                {
                    var check = new CheckBox
                    {
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        MinWidth = 0,
                        Padding = new Thickness(0),
                        Margin = new Thickness(12, 0, 0, 0)
                    };

                    // Saving state of the row (Internal Database): amber = not saved
                    // yet, red = couldn't be saved (tooltip says why).
                    var mark = new Avalonia.Controls.Shapes.Ellipse
                    {
                        Width = 7,
                        Height = 7,
                        Margin = new Thickness(6, 0, 0, 0),
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                        IsVisible = false,
                        Tag = DatabaseRowMarkTag.Instance,
                    };

                    var cell = new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    };
                    cell.Children.Add(check);
                    cell.Children.Add(mark);

                    var syncing = false;

                    void Sync()
                    {
                        syncing = true;
                        check.IsChecked = check.DataContext is DataRowView row && tab.IsDatabaseRowChecked(row.Row);
                        syncing = false;
                    }

                    // Recycled cells get a new DataContext: re-read the tick for the new row.
                    check.DataContextChanged += (_, _) => Sync();
                    mark.DataContextChanged += (_, _) => UpdateRowMark(tab, mark);

                    check.IsCheckedChanged += (_, _) =>
                    {
                        if (syncing) return;
                        if (check.DataContext is DataRowView row)
                            tab.SetDatabaseRowChecked(row.Row, check.IsChecked == true);
                    };

                    _rowCheckSyncs.Add(Sync);
                    return cell;
                },
                supportsRecycling: true)
        };
    }


    /// <summary>Image column: shows a thumbnail, or "Choose..." when empty.
    /// Clicking opens the file picker for that row.</summary>
    //private static DataGridTemplateColumn CreateImageColumn(CardDesignTabViewModel tab, string columnName)
    //{
    //    return new DataGridTemplateColumn
    //    {
    //        Header = columnName,
    //        Width = new DataGridLength(90),

    //        CellTemplate = new FuncDataTemplate<object>(
    //            (_, _) =>
    //            {
    //                var image = new Image
    //                {
    //                    Width = 32,
    //                    Height = 32,
    //                    Stretch = Avalonia.Media.Stretch.Uniform
    //                };

    //                var placeholder = new TextBlock
    //                {
    //                    Text = "Choose...",
    //                    FontSize = 12,
    //                    Opacity = 0.6,
    //                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
    //                };

    //                var content = new Panel();
    //                content.Children.Add(image);
    //                content.Children.Add(placeholder);

    //                var button = new Button
    //                {
    //                    Content = content,
    //                    Padding = new Thickness(4, 2),
    //                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
    //                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
    //                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
    //                };
    //                button.Classes.Add("ghost");
    //                ToolTip.SetTip(button, "Click to choose an image");

    //                void Refresh()
    //                {
    //                    var bitmap = button.DataContext is DataRowView row
    //                        ? tab.GetDatabaseImage(GetCellValue(row, columnName))
    //                        : null;

    //                    image.Source = bitmap;
    //                    image.IsVisible = bitmap is not null;
    //                    placeholder.IsVisible = bitmap is null;
    //                }

    //                // Recycled cells get a new row: show that row's image.
    //                button.DataContextChanged += (_, _) => Refresh();

    //                button.Click += async (_, _) =>
    //                {
    //                    if (button.DataContext is DataRowView row)
    //                        await tab.PickDatabaseImageAsync(row, columnName);
    //                };

    //                return button;
    //            },
    //            supportsRecycling: false)
    //    };
    //}


    private static DataGridTemplateColumn CreateImageColumn(CardDesignTabViewModel tab, string columnName)
    {
        return new DataGridTemplateColumn
        {
            Header = columnName,
            Width = new DataGridLength(120), // wider: thumbnail + two icon buttons

            CellTemplate = new FuncDataTemplate<object>(
                (_, _) =>
                {
                    var image = new Image
                    {
                        Width = 32,
                        Height = 32,
                        Stretch = Avalonia.Media.Stretch.Uniform,
                        IsVisible = false
                    };

                    var browseButton = CreateIconButton(DesignerIcons.Image, "Choose image from file");
                    var cameraButton = CreateIconButton(DesignerIcons.Camera, "Take photo with camera");

                    var panel = new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 4,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };
                    panel.Children.Add(image);
                    panel.Children.Add(browseButton);
                    panel.Children.Add(cameraButton);

                    void Refresh()
                    {
                        var bitmap = panel.DataContext is DataRowView row
                            ? tab.GetDatabaseImage(GetCellValue(row, columnName))
                            : null;

                        image.Source = bitmap;
                        image.IsVisible = bitmap is not null;
                    }

                    // Recycled cells get a new row: show that row's image.
                    panel.DataContextChanged += (_, _) => Refresh();

                    browseButton.Click += async (_, _) =>
                    {
                        if (panel.DataContext is DataRowView row)
                            await tab.PickDatabaseImageAsync(row, columnName);
                    };

                    cameraButton.Click += async (_, _) =>
                    {
                        if (panel.DataContext is DataRowView row)
                            await tab.CaptureDatabaseImageAsync(row, columnName);
                    };

                    return panel;
                },
                supportsRecycling: false)
        };
    }

    private static Button CreateIconButton(Avalonia.Media.Geometry icon, string tip)
    {
        var button = new Button
        {
            Content = new PathIcon { Data = icon, Width = 16, Height = 16 },
            Padding = new Thickness(4, 2),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };
        button.Classes.Add("ghost");
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        return button;
    }
    private static object? GetCellValue(DataRowView row, string columnName)
    {
        if (row.Row.RowState == DataRowState.Detached ||
            !row.Row.Table.Columns.Contains(columnName))
            return null;

        var value = row[columnName];
        return value == DBNull.Value ? null : value;
    }


    /// <summary>Refreshes every visible checkbox and the header after ticks change
    /// (select-all, delete, search, or a single tick).</summary>
    private void OnDatabaseRowChecksChanged(object? sender, EventArgs e)
    {
        foreach (var sync in _rowCheckSyncs)
            sync();

        if (_headerCheckBox is not null && sender is CardDesignTabViewModel tab)
        {
            _syncingHeaderCheck = true;
            _headerCheckBox.IsChecked = tab.AreAllVisibleDatabaseRowsChecked;
            _syncingHeaderCheck = false;
        }
    }


    private CardDesignTabViewModel? _saveEventsTab;

    /// <summary>Marks the text cells of the data grid (so saved rows can be refreshed
    /// in place without rebuilding the grid, which would interrupt typing).</summary>
    private sealed record DatabaseCellTag(string Column);

    /// <summary>Marks the per-row "unsaved / not saved" dot in the tick column.</summary>
    private sealed class DatabaseRowMarkTag
    {
        public static readonly DatabaseRowMarkTag Instance = new();
    }

    private static void UpdateRowMark(CardDesignTabViewModel tab, Avalonia.Controls.Shapes.Ellipse mark)
    {
        string? tip = null;
        var state = mark.DataContext is DataRowView row ? tab.GetDatabaseRowMark(row.Row, out tip) : null;

        mark.IsVisible = state is not null;
        if (state is not null)
        {
            mark.Fill = mark.FindResource(state == "error" ? "BrushError" : "BrushWarning") as Avalonia.Media.IBrush
                        ?? (state == "error" ? Avalonia.Media.Brushes.IndianRed : Avalonia.Media.Brushes.Orange);
        }
        ToolTip.SetTip(mark, tip);
    }

    /// <summary>After saving (or any change of the unsaved count): refresh the visible
    /// cells' text (new IDs instead of "New") and the row dots.</summary>
    private void RefreshDatabaseGridCells()
    {
        var tab = ViewModel?.ActiveTab;
        if (DatabaseDataGrid is null || tab is null) return;

        foreach (var visual in DatabaseDataGrid.GetVisualDescendants())
        {
            switch (visual)
            {
                case TextBlock { Tag: DatabaseCellTag cellTag } textBlock:
                    textBlock.Text = tab.GetDatabaseCellText(textBlock.DataContext as DataRowView, cellTag.Column);
                    break;
                case Avalonia.Controls.Shapes.Ellipse { Tag: DatabaseRowMarkTag } mark:
                    UpdateRowMark(tab, mark);
                    break;
            }
        }
    }

    private void OnDatabaseRowsSaved(object? sender, EventArgs e)
    {
        if (ReferenceEquals(sender, ViewModel?.ActiveTab))
            RefreshDatabaseGridCells();
    }

    /// <summary>Focus left the data grid (clicked the canvas, a panel, a menu...):
    /// the row being edited is finished, so save it (Internal Database).</summary>
    private void OnDatabaseGridLostFocus(object? sender, RoutedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DatabaseDataGrid is null || DatabaseDataGrid.IsKeyboardFocusWithin) return;
            ViewModel?.ActiveTab?.QueueDatabaseAutoSave(includeCurrentRow: true);
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// Commits any pending edits in the DatabaseDataGrid by moving focus away from
    /// the current cell. This ensures that if the user was typing in a TextBox,
    /// the LostFocus event fires and the value is copied into the DataRow before
    /// we try to save the row to SQLite.
    /// </summary>
    public void CommitDatabaseGridEdit()
    {
        if (DatabaseDataGrid is null)
            return;

        // Avalonia DataGrid doesn't have a built-in CommitEdit() like WPF.
        // Workaround: Move focus away from the DataGrid to trigger LostFocus on any active TextBox.
        // This is safe because the "+ Add row" button click will naturally move focus anyway.
        DatabaseDataGrid.Focus();
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
