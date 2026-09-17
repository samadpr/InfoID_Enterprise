using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Database.Models;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using System.Collections.ObjectModel;

namespace InfoID.Desktop.Features.Database.ViewModels;

public sealed partial class AddColumnDialogViewModel
    : DialogViewModelBase<DatabaseColumn?>
{
    public override string Title => "Add column";

    public ObservableCollection<string> DataTypes { get; } =
        new()
        {
            "VARCHAR",
            "INTEGER",
            "DECIMAL",
            "DATE",
            "DATETIME",
            "BOOLEAN"
        };

    [ObservableProperty]
    private string _columnName = string.Empty;

    [ObservableProperty]
    private string _selectedDataType = "VARCHAR";

    [ObservableProperty]
    private bool _isPrimaryKey;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [RelayCommand]
    private void Add()
    {
        if (string.IsNullOrWhiteSpace(ColumnName))
        {
            StatusMessage = "Please enter a column name.";
            return;
        }

        var column = new DatabaseColumn
        {
            Name = ColumnName.Trim(),
            DataType = SelectedDataType,
            IsPrimaryKey = IsPrimaryKey,
            IsSelected = true
        };

        RequestClose(column);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose(null);
    }
}
