using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Licensing.Core;
using InfoID.Licensing.Server.Entities;
using InfoID.Licensing.Server.Services;

namespace InfoID.AdminLicenseManager.ViewModels;

/// <summary>
/// License Requests inbox: plan requests sent from the app. Staff confirm payment outside
/// the system, then Approve -- the activation code is created from the (pre-filled,
/// editable) terms, linked to the customer's account and emailed. Or Reject with a reason
/// the customer sees.
/// </summary>
public sealed partial class LicenseRequestsViewModel : ViewModelBase, ILoadable
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<LicenseRequestsViewModel>();

    private const string OpenFilter = "Open (New + In progress)";
    private const string AllFilter = "All";

    private readonly LicenseRequestService _requests;

    public LicenseRequestsViewModel(LicenseRequestService requests, AdminContext admin)
    {
        _requests = requests;
        CanManage = admin.CanManageLicenses;
    }

    public bool CanManage { get; }

    // ------------------------------------------------------------ list ----

    public ObservableCollection<LicenseRequestRow> Rows { get; } = new();

    public string[] StatusFilters { get; } =
    [
        OpenFilter, LicenseRequestStatus.New, LicenseRequestStatus.InProgress,
        LicenseRequestStatus.Approved, LicenseRequestStatus.Rejected, AllFilter,
    ];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _statusFilter = OpenFilter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(IsSelectedOpen), nameof(IsSelectedApproved), nameof(IsSelectedRejected), nameof(SelectedSummary))]
    private LicenseRequestRow? _selectedRow;

    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;

    public bool HasSelection => SelectedRow is not null;
    public bool IsSelectedOpen => SelectedRow?.Status is LicenseRequestStatus.New or LicenseRequestStatus.InProgress;
    public bool IsSelectedApproved => SelectedRow?.Status == LicenseRequestStatus.Approved;
    public bool IsSelectedRejected => SelectedRow?.Status == LicenseRequestStatus.Rejected;

    public string SelectedSummary => SelectedRow is not { } r ? "" :
        $"Request #{r.Id}  ·  {r.CreatedDate.ToLocalTime():g}  ·  {r.Status}" +
        (r.HandledBy is not null ? $"  ·  handled by {r.HandledBy}" : "");

    public async Task LoadAsync() => await Search();

    [RelayCommand]
    private async Task Search()
    {
        try
        {
            var status = StatusFilter is OpenFilter or AllFilter ? null : StatusFilter;
            var rows = await _requests.SearchAsync(status, SearchText);
            if (StatusFilter == OpenFilter)
                rows = rows.Where(r => r.Status is LicenseRequestStatus.New or LicenseRequestStatus.InProgress).ToList();

            var keep = SelectedRow?.Id;
            Rows.Clear();
            foreach (var row in rows) Rows.Add(row);
            SelectedRow = Rows.FirstOrDefault(r => r.Id == keep);
        }
        catch (Exception ex)
        {
            Log.Error("LicenseRequests: search failed", ex);
            Show("Loading requests failed: " + ex.GetBaseException().Message, error: true);
        }
    }

    partial void OnStatusFilterChanged(string value) => _ = Search();

    // ------------------------------------------------------------ approve form ----

    public LicenseTier[] Tiers { get; } = Enum.GetValues<LicenseTier>();
    public LicenseType[] Types { get; } = [LicenseType.Subscription, LicenseType.Perpetual];
    public CardLimitPeriod[] LimitPeriods { get; } = Enum.GetValues<CardLimitPeriod>();

    [ObservableProperty] private LicenseTier _tier = LicenseTier.Standard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsExpiry))]
    private LicenseType _type = LicenseType.Subscription;

    [ObservableProperty] private DateTime? _expiryDate = DateTime.Today.AddYears(1);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCardLimit))]
    private bool _unlimitedCards = true;

    [ObservableProperty] private decimal? _maxCardsPerPeriod = 1000;
    [ObservableProperty] private CardLimitPeriod _limitPeriod = CardLimitPeriod.Monthly;
    [ObservableProperty] private decimal? _maxActivations = 1;
    [ObservableProperty] private bool _allowOfflineActivation;
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private string _rejectReason = "";

    public bool NeedsExpiry => Type != LicenseType.Perpetual;
    public bool HasCardLimit => !UnlimitedCards;

    /// <summary>Pre-fill the approval terms from the plan and the request.</summary>
    partial void OnSelectedRowChanged(LicenseRequestRow? value)
    {
        if (value is null) return;
        Tier = Enum.TryParse<LicenseTier>(value.PlanTier, out var tier) ? tier : LicenseTier.Standard;
        Type = value.PlanDurationMonths is null ? LicenseType.Perpetual : LicenseType.Subscription;
        ExpiryDate = DateTime.Today.AddMonths(value.PlanDurationMonths ?? 12);
        UnlimitedCards = value.PlanMaxCards is null;
        MaxCardsPerPeriod = value.PlanMaxCards ?? 1000;
        LimitPeriod = Enum.TryParse<CardLimitPeriod>(value.PlanLimitPeriod, out var period) ? period : CardLimitPeriod.Monthly;
        MaxActivations = value.Machines;
        AllowOfflineActivation = false;
        Notes = "";
        RejectReason = "";
    }

    [RelayCommand]
    private async Task MarkInProgress()
    {
        if (SelectedRow is not { } row) return;
        try
        {
            await _requests.MarkInProgressAsync(row.Id);
            Show($"Request #{row.Id} marked as in progress.", error: false);
            await Search();
        }
        catch (Exception ex)
        {
            Log.Error("LicenseRequests: mark in progress failed", ex);
            Show(ex.GetBaseException().Message, error: true);
        }
    }

    [RelayCommand]
    private async Task Approve()
    {
        if (SelectedRow is not { } row) return;
        try
        {
            var code = await _requests.ApproveAsync(new ApproveRequestInput(
                row.Id,
                Tier,
                Type,
                NeedsExpiry && ExpiryDate is { } expiry ? DateOnly.FromDateTime(expiry) : null,
                UnlimitedCards ? null : (int?)(MaxCardsPerPeriod ?? 0),
                LimitPeriod,
                (int)(MaxActivations ?? 1),
                AllowOfflineActivation,
                Notes));
            Show($"Approved. Activation code {code} was created and emailed to {row.Email}" +
                 (row.HasAccount ? " (it also appears under \"My licenses\" in the app)." : "."), error: false);
            await Search();
        }
        catch (Exception ex)
        {
            Log.Error("LicenseRequests: approve failed", ex);
            Show(ex.GetBaseException().Message, error: true);
        }
    }

    [RelayCommand]
    private async Task Reject()
    {
        if (SelectedRow is not { } row) return;
        try
        {
            await _requests.RejectAsync(row.Id, RejectReason);
            Show($"Request #{row.Id} rejected. The customer was emailed the reason.", error: false);
            await Search();
        }
        catch (Exception ex)
        {
            Log.Error("LicenseRequests: reject failed", ex);
            Show(ex.GetBaseException().Message, error: true);
        }
    }

    private void Show(string message, bool error)
    {
        Message = message;
        MessageIsError = error;
    }
}
