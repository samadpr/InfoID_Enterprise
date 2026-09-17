using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Database.Models;

namespace InfoID.Desktop.Features.Database.ViewModels;

public sealed partial class DatabaseTypeSelectionDialogViewModel
    : DialogViewModelBase<DatabaseType?>
{
    public override string Title => "Connect to database...";

    [RelayCommand]
    private void SelectDatabase(DatabaseType databaseType)
    {
        RequestClose(databaseType);
    }

    [RelayCommand]
    private void Cancel()
    {
        RequestClose(null);
    }
}
