using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Licensing.Core;
using InfoID.Licensing.Server.Entities;
using InfoID.Licensing.Server.Services;

namespace InfoID.AdminLicenseManager.ViewModels;

/// <summary>Plans shown on the app's Editions page. Changes reach every customer's app
/// immediately (the app loads plans from the server) -- no new app release needed.</summary>
public sealed partial class PlansViewModel : ViewModelBase, ILoadable
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<PlansViewModel>();

    private readonly PlanService _plans;

    public PlansViewModel(PlanService plans, AdminContext admin)
    {
        _plans = plans;
        CanManage = admin.CanManageLicenses;
    }

    public bool CanManage { get; }

    public ObservableCollection<Plan> Rows { get; } = new();

    public LicenseTier[] Tiers { get; } = Enum.GetValues<LicenseTier>();
    public CardLimitPeriod[] LimitPeriods { get; } = Enum.GetValues<CardLimitPeriod>();

    [ObservableProperty] private Plan? _selectedRow;
    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;

    // ------------------------------------------------------------ edit form ----

    [ObservableProperty] private long? _editingId;
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private LicenseTier _tier = LicenseTier.Standard;
    [ObservableProperty] private string _tagline = "";
    [ObservableProperty] private string _featuresText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDuration))]
    private bool _perpetual;

    [ObservableProperty] private decimal? _durationMonths = 12;
    [ObservableProperty] private decimal? _defaultMachines = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCardLimit))]
    private bool _unlimitedCards = true;

    [ObservableProperty] private decimal? _maxCardsPerPeriod = 1000;
    [ObservableProperty] private CardLimitPeriod _limitPeriod = CardLimitPeriod.Monthly;
    [ObservableProperty] private string _priceText = "Contact sales";
    [ObservableProperty] private decimal? _sortOrder = 1;
    [ObservableProperty] private bool _isActive = true;

    public bool HasDuration => !Perpetual;
    public bool HasCardLimit => !UnlimitedCards;
    public string FormTitle => EditingId is null ? "New plan" : "Edit plan";

    partial void OnEditingIdChanged(long? value) => OnPropertyChanged(nameof(FormTitle));

    public async Task LoadAsync()
    {
        try
        {
            var keep = SelectedRow?.Id;
            Rows.Clear();
            foreach (var plan in await _plans.GetAllAsync()) Rows.Add(plan);
            SelectedRow = Rows.FirstOrDefault(p => p.Id == keep);
        }
        catch (Exception ex)
        {
            Log.Error("Plans: load failed", ex);
            Show("Loading plans failed: " + ex.GetBaseException().Message, error: true);
        }
    }

    partial void OnSelectedRowChanged(Plan? value)
    {
        if (value is null) return;
        EditingId = value.Id;
        Name = value.Name;
        Tier = Enum.TryParse<LicenseTier>(value.Tier, out var tier) ? tier : LicenseTier.Standard;
        Tagline = value.Tagline ?? "";
        FeaturesText = value.FeaturesText ?? "";
        Perpetual = value.DurationMonths is null;
        DurationMonths = value.DurationMonths ?? 12;
        DefaultMachines = value.DefaultMachines;
        UnlimitedCards = value.MaxCardsPerPeriod is null;
        MaxCardsPerPeriod = value.MaxCardsPerPeriod ?? 1000;
        LimitPeriod = Enum.TryParse<CardLimitPeriod>(value.CardLimitPeriod, out var period) ? period : CardLimitPeriod.Monthly;
        PriceText = value.PriceText ?? "";
        SortOrder = value.SortOrder;
        IsActive = value.IsActive;
    }

    [RelayCommand]
    private void New()
    {
        SelectedRow = null;
        EditingId = null;
        Name = "";
        Tier = LicenseTier.Standard;
        Tagline = "";
        FeaturesText = "";
        Perpetual = false;
        DurationMonths = 12;
        DefaultMachines = 1;
        UnlimitedCards = true;
        MaxCardsPerPeriod = 1000;
        LimitPeriod = CardLimitPeriod.Monthly;
        PriceText = "Contact sales";
        SortOrder = Rows.Count + 1;
        IsActive = true;
        Message = null;
    }

    [RelayCommand]
    private async Task Save()
    {
        try
        {
            var saved = await _plans.SaveAsync(new PlanEdit(
                EditingId,
                Name,
                Tier,
                Tagline,
                FeaturesText,
                Perpetual ? null : (int?)(DurationMonths ?? 12),
                (int)(DefaultMachines ?? 1),
                UnlimitedCards ? null : (int?)(MaxCardsPerPeriod ?? 0),
                LimitPeriod,
                PriceText,
                (int)(SortOrder ?? 0),
                IsActive));
            Show($"Plan \"{saved.Name}\" saved. Customers see the change the next time they open the Editions page.", error: false);
            EditingId = saved.Id;
            await LoadAsync();
            SelectedRow = Rows.FirstOrDefault(p => p.Id == saved.Id);
        }
        catch (Exception ex)
        {
            Log.Error("Plans: save failed", ex);
            Show(ex.GetBaseException().Message, error: true);
        }
    }

    private void Show(string message, bool error)
    {
        Message = message;
        MessageIsError = error;
    }
}
