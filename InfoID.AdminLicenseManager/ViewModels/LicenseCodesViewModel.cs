using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Licensing.Core;
using InfoID.Licensing.Server.Entities;
using InfoID.Licensing.Server.Services;

namespace InfoID.AdminLicenseManager.ViewModels;

/// <summary>Activation Codes: create single/batch codes (FR-ADM-4), search (FR-ADM-7)
/// and the first license actions -- revoke and extend (FR-ADM-8).</summary>
public sealed partial class LicenseCodesViewModel : ViewModelBase, ILoadable
{
    private readonly LicenseCodeService _codes;

    public LicenseCodesViewModel(LicenseCodeService codes, AdminContext admin)
    {
        _codes = codes;
        CanManage = admin.CanManageLicenses;
    }

    public bool CanManage { get; }

    // ------------------------------------------------------------ search / list ----

    public ObservableCollection<LicenseCodeRow> Rows { get; } = new();

    public string[] StatusFilters { get; } = ["All", LicenseCodeStatus.Available, LicenseCodeStatus.Active, LicenseCodeStatus.Revoked];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _statusFilter = "All";
    [ObservableProperty] private LicenseCodeRow? _selectedRow;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;

    public async Task LoadAsync() => await Search();

    [RelayCommand]
    private async Task Search()
    {
        try
        {
            var rows = await _codes.SearchAsync(SearchText, StatusFilter == "All" ? null : StatusFilter);
            Rows.Clear();
            foreach (var row in rows) Rows.Add(row);
        }
        catch (Exception ex)
        {
            Show("Search failed: " + ex.GetBaseException().Message, error: true);
        }
    }

    partial void OnStatusFilterChanged(string value) => _ = Search();

    // ------------------------------------------------------------ create form ----

    public LicenseTier[] Tiers { get; } = Enum.GetValues<LicenseTier>();
    public LicenseType[] Types { get; } = [LicenseType.Subscription, LicenseType.Perpetual];
    public CardLimitPeriod[] LimitPeriods { get; } = Enum.GetValues<CardLimitPeriod>();

    [ObservableProperty] private decimal? _quantity = 1;
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
    [ObservableProperty] private string _organizationName = "";
    [ObservableProperty] private string _organizationType = "";
    [ObservableProperty] private string _country = "";
    [ObservableProperty] private string _contactEmail = "";
    [ObservableProperty] private string _contactPhone = "";
    [ObservableProperty] private string _notes = "";

    /// <summary>Codes from the last Create, one per line, ready to copy or email.</summary>
    [ObservableProperty] private string _createdCodes = "";

    public bool NeedsExpiry => Type != LicenseType.Perpetual;
    public bool HasCardLimit => !UnlimitedCards;

    [RelayCommand]
    private async Task Create()
    {
        try
        {
            var request = new CreateLicenseCodesRequest(
                (int)(Quantity ?? 1),
                Tier,
                Type,
                NeedsExpiry && ExpiryDate is { } expiry ? DateOnly.FromDateTime(expiry) : null,
                UnlimitedCards ? null : (int?)(MaxCardsPerPeriod ?? 0),
                LimitPeriod,
                (int)(MaxActivations ?? 1),
                OrganizationName, OrganizationType, Country, ContactEmail, ContactPhone, Notes);

            var codes = await _codes.CreateAsync(request);
            CreatedCodes = string.Join(Environment.NewLine, codes);
            Show($"Created {codes.Count} activation code{(codes.Count == 1 ? "" : "s")}.", error: false);
            await Search();
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            Show(ex.Message, error: true);
        }
        catch (Exception ex)
        {
            Show("Couldn't create codes: " + ex.GetBaseException().Message, error: true);
        }
    }

    // ------------------------------------------------------------ actions ----

    [ObservableProperty] private string _actionReason = "";
    [ObservableProperty] private DateTime? _newExpiryDate = DateTime.Today.AddYears(1);

    [RelayCommand]
    private async Task Revoke()
    {
        if (SelectedRow is null) { Show("Select a code first.", error: true); return; }
        if (string.IsNullOrWhiteSpace(ActionReason)) { Show("Enter a reason for revoking.", error: true); return; }
        try
        {
            await _codes.RevokeAsync(SelectedRow.Id, ActionReason.Trim());
            Show($"Revoked {SelectedRow.ActivationCode}. It stops working at the customer's next online re-validation.", error: false);
            ActionReason = "";
            await Search();
        }
        catch (Exception ex)
        {
            Show(ex.GetBaseException().Message, error: true);
        }
    }

    [RelayCommand]
    private async Task Extend()
    {
        if (SelectedRow is null) { Show("Select a code first.", error: true); return; }
        if (NewExpiryDate is not { } date) { Show("Choose the new expiry date.", error: true); return; }
        try
        {
            await _codes.ExtendAsync(SelectedRow.Id, DateOnly.FromDateTime(date));
            Show($"Extended {SelectedRow.ActivationCode} to {date:d}. Generate a new serial key for machines that activated offline.", error: false);
            await Search();
        }
        catch (Exception ex)
        {
            Show(ex.GetBaseException().Message, error: true);
        }
    }

    private void Show(string message, bool error)
    {
        Message = message;
        MessageIsError = error;
    }
}
