using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;

namespace InfoID.Desktop.Features.Database.ViewModels;

public sealed partial class ConnectDatabaseDialogViewModel
    : DialogViewModelBase<bool>
{
    public override string Title => "Connect to database";

    [RelayCommand]
    private void Cancel()
    {
        RequestClose(false);
    }

    [RelayCommand]
    private void Connect()
    {
        // Temporary — actual database connection comes next.
        RequestClose(true);
    }
}