using System.Data;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;

namespace InfoID.Desktop.Features.Database.ViewModels;

public sealed partial class ExcelPreviewDialogViewModel
    : DialogViewModelBase<bool>
{
    public DataTable Data { get; }

    public string FileName { get; }

    public string SheetName { get; }

    public ExcelPreviewDialogViewModel(
        DataTable data,
        string fileName,
        string sheetName)
    {
        Data = data;
        FileName = fileName;
        SheetName = sheetName;
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose(true);
    }
}
