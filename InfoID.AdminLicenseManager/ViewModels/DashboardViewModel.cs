using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Licensing.Server.Services;

namespace InfoID.AdminLicenseManager.ViewModels;

public sealed partial class DashboardViewModel : ViewModelBase, ILoadable
{
    private readonly DashboardService _dashboard;

    [ObservableProperty] private DashboardSummary? _summary;
    [ObservableProperty] private string? _error;

    public DashboardViewModel(DashboardService dashboard) => _dashboard = dashboard;

    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            Error = null;
            Summary = await _dashboard.GetSummaryAsync();
        }
        catch (Exception ex)
        {
            Error = "Couldn't load the dashboard: " + ex.GetBaseException().Message;
        }
    }
}
