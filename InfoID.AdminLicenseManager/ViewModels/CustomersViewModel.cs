using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Licensing.Server.Services;

namespace InfoID.AdminLicenseManager.ViewModels;

/// <summary>
/// Customers: every account created in the app. Leads (signed up and verified, nothing
/// requested yet) are a filter of their own for sales follow-up. Selecting a customer
/// shows their requests and licenses; staff can resend the verification email or
/// disable/enable the account.
/// </summary>
public sealed partial class CustomersViewModel : ViewModelBase, ILoadable
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<CustomersViewModel>();

    private readonly CustomerAdminService _customers;
    private readonly CustomerAccountService _accounts;

    public CustomersViewModel(CustomerAdminService customers, CustomerAccountService accounts, AdminContext admin)
    {
        _customers = customers;
        _accounts = accounts;
        CanManage = admin.CanManageLicenses;
    }

    public bool CanManage { get; }

    public ObservableCollection<CustomerRow> Rows { get; } = new();
    public ObservableCollection<AccountLicense> Licenses { get; } = new();
    public ObservableCollection<AccountRequest> Requests { get; } = new();

    public string[] Filters { get; } = CustomerFilters.Values;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _filter = CustomerFilters.All;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(CanResend), nameof(CanDisable), nameof(CanEnable), nameof(SelectedSummary))]
    private CustomerRow? _selectedRow;

    [ObservableProperty] private string _disableReason = "";
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;
    [ObservableProperty] private string _totals = "";

    public bool HasSelection => SelectedRow is not null;
    public bool CanResend => SelectedRow is { EmailVerified: false, Disabled: false };
    public bool CanDisable => CanManage && SelectedRow is { Disabled: false };
    public bool CanEnable => CanManage && SelectedRow is { Disabled: true };

    public string SelectedSummary => SelectedRow is not { } c ? "" :
        $"{c.StatusText}  ·  signed up {c.CreatedDate.ToLocalTime():d}" +
        (c.LastLoginDate is { } last ? $"  ·  last login {last.ToLocalTime():g}" : "  ·  never logged in");

    public async Task LoadAsync() => await Search();

    [RelayCommand]
    private async Task Search()
    {
        try
        {
            var keep = SelectedRow?.Id;
            var rows = await _customers.SearchAsync(Filter, SearchText);
            Rows.Clear();
            foreach (var row in rows) Rows.Add(row);
            SelectedRow = Rows.FirstOrDefault(r => r.Id == keep);

            Totals = $"{rows.Count} shown  ·  {rows.Count(r => r.IsLead)} lead(s)  ·  {rows.Count(r => !r.EmailVerified && !r.Disabled)} not verified";
        }
        catch (Exception ex)
        {
            Log.Error("Customers: search failed", ex);
            Show("Loading customers failed: " + ex.GetBaseException().Message, error: true);
        }
    }

    partial void OnFilterChanged(string value) => _ = Search();

    partial void OnSelectedRowChanged(CustomerRow? value)
    {
        DisableReason = "";
        Licenses.Clear();
        Requests.Clear();
        if (value is not null) _ = LoadDetailsAsync(value.Id);
    }

    private async Task LoadDetailsAsync(long accountId)
    {
        try
        {
            var licenses = await _accounts.GetLicensesAsync(accountId);
            var requests = await _accounts.GetRequestsAsync(accountId);
            if (SelectedRow?.Id != accountId) return; // selection changed meanwhile
            foreach (var license in licenses) Licenses.Add(license);
            foreach (var request in requests) Requests.Add(request);
        }
        catch (Exception ex)
        {
            Log.Error("Customers: load details failed", ex);
            Show("Loading details failed: " + ex.GetBaseException().Message, error: true);
        }
    }

    [RelayCommand]
    private async Task ResendVerification()
    {
        if (SelectedRow is not { } row) return;
        try
        {
            var result = await _accounts.ResendVerificationAsync(row.Email);
            Show(result.Succeeded ? $"A new verification code was emailed to {row.Email}." : result.Error ?? "Couldn't send the code.", !result.Succeeded);
        }
        catch (Exception ex)
        {
            Log.Error("Customers: resend verification failed", ex);
            Show(ex.GetBaseException().Message, error: true);
        }
    }

    [RelayCommand]
    private async Task Disable()
    {
        if (SelectedRow is not { } row) return;
        try
        {
            await _customers.SetDisabledAsync(row.Id, true, DisableReason);
            Show($"{row.Email} is disabled and was signed out everywhere.", error: false);
            await Search();
        }
        catch (Exception ex)
        {
            Log.Error("Customers: disable failed", ex);
            Show(ex.GetBaseException().Message, error: true);
        }
    }

    [RelayCommand]
    private async Task Enable()
    {
        if (SelectedRow is not { } row) return;
        try
        {
            await _customers.SetDisabledAsync(row.Id, false, null);
            Show($"{row.Email} is enabled again.", error: false);
            await Search();
        }
        catch (Exception ex)
        {
            Log.Error("Customers: enable failed", ex);
            Show(ex.GetBaseException().Message, error: true);
        }
    }

    private void Show(string message, bool error)
    {
        Message = message;
        MessageIsError = error;
    }
}
