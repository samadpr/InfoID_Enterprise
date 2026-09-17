using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System;
using System.Data;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Database.ViewModels;

namespace InfoID.Desktop.Features.Database.Views;

public partial class ConnectDatabaseDialogView : UserControl
{
    public ConnectDatabaseDialogView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(
        object? sender,
        EventArgs e)
    {
        if (DataContext is ConnectDatabaseDialogViewModel viewModel)
        {
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
        }
    }

    private void ViewModel_PropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectDatabaseDialogViewModel.LoadedData))
        {
            if (DataContext is ConnectDatabaseDialogViewModel viewModel)
            {
                BuildDataGrid(viewModel.LoadedData);
            }
        }
    }

    private void BuildDataGrid(DataTable? table)
    {
        ExcelPreviewGrid.Columns.Clear();

        if (table is null)
        {
            ExcelPreviewGrid.ItemsSource = null;
            return;
        }

        foreach (DataColumn column in table.Columns)
        {
            var columnName = column.ColumnName;

            var gridColumn = new DataGridTemplateColumn
            {
                Header = columnName,

                CellTemplate = new FuncDataTemplate<ExcelPreviewRow>(
                    (row, _) =>
                    {
                        var text = "";

                        if (row.Values.TryGetValue(
                                columnName,
                                out var value))
                        {
                            text = value == DBNull.Value
                                ? ""
                                : value?.ToString() ?? "";
                        }

                        return new TextBlock
                        {
                            Text = text,
                            Margin = new Avalonia.Thickness(6, 0)
                        };
                    },
                    supportsRecycling: true)
            };

            ExcelPreviewGrid.Columns.Add(gridColumn);
        }

        if (DataContext is ConnectDatabaseDialogViewModel viewModel)
        {
            ExcelPreviewGrid.ItemsSource = viewModel.PreviewRows;
        }
    }
}
