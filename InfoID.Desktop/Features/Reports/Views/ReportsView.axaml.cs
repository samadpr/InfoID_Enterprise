using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using InfoID.Application.Reporting;
using InfoID.Desktop.Features.Reports.ViewModels;

namespace InfoID.Desktop.Features.Reports.Views;

/// <summary>Reports differ in their columns, so the grid's columns are built here each
/// time a new report is produced (the rows are plain formatted cells).</summary>
public partial class ReportsView : UserControl
{
    private ReportsViewModel? _viewModel;
    private ReportTable? _builtFor;

    public ReportsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as ReportsViewModel);
    }

    private void Attach(ReportsViewModel? viewModel)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = viewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BuildColumns();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportsViewModel.Table)) BuildColumns();
    }

    private void BuildColumns()
    {
        var table = _viewModel?.Table;
        if (ReferenceEquals(table, _builtFor)) return;
        _builtFor = table;

        ReportGrid.Columns.Clear();
        if (table is null) return;

        for (var i = 0; i < table.Columns.Count; i++)
        {
            var column = table.Columns[i];
            ReportGrid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Header,
                // Reflection binding to the row's cell array (columns vary per report).
                Binding = new Binding($"{nameof(ReportGridRow.Cells)}[{i}]") { Mode = BindingMode.OneWay },
                IsReadOnly = true,
                Width = new DataGridLength(column.Width, DataGridLengthUnitType.Star),
                MinWidth = column.Type == ReportColumnType.DateTime ? 130 : 70,
            });
        }
    }
}
