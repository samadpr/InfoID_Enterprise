using System;
using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace InfoID.Desktop.Features.Database.Views;

public partial class ExcelPreviewDialogView : UserControl
{
    public ExcelPreviewDialogView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not ViewModels.ExcelPreviewDialogViewModel viewModel)
            return;

        BuildColumns(viewModel.Data);
    }

    private void BuildColumns(DataTable? data)
    {
        if (PreviewGrid is null)
            return;

        PreviewGrid.Columns.Clear();

        if (data is null)
            return;

        foreach (DataColumn column in data.Columns)
        {
            var columnName = column.ColumnName;

            PreviewGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = columnName,

                // Read the value straight from the row. Avalonia can't bind
                // "[ColumnName]" on a DataRowView the way WPF can.
                CellTemplate = new FuncDataTemplate<object>(
                    (item, _) =>
                    {
                        var textBlock = new TextBlock
                        {
                            Text = GetCellText(item, columnName),
                            Margin = new Thickness(6, 0),
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };

                        // Recycled cells get a new row: show that row's value.
                        textBlock.DataContextChanged += (_, _) =>
                            textBlock.Text = GetCellText(textBlock.DataContext, columnName);

                        return textBlock;
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
}
