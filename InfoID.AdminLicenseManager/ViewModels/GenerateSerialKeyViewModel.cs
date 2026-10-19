using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Licensing.Core;
using InfoID.Licensing.Server.Services;

namespace InfoID.AdminLicenseManager.ViewModels;

/// <summary>Serial Key Generation from Soft Code (FR-ADM-6): paste the customer's
/// request code, see what it decodes to and whether it can be activated, optionally
/// override tier/expiry, generate, copy. SRS target: under 60 seconds.</summary>
public sealed partial class GenerateSerialKeyViewModel : ViewModelBase
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<GenerateSerialKeyViewModel>();

    private readonly ActivationService _activation;

    public GenerateSerialKeyViewModel(ActivationService activation, AdminContext admin)
    {
        _activation = activation;
        CanOverride = admin.CanManageLicenses;
    }

    public bool CanOverride { get; }

    public LicenseTier[] Tiers { get; } = Enum.GetValues<LicenseTier>();

    [ObservableProperty] private string _softCodeText = "";
    [ObservableProperty] private SoftCodePreview? _preview;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private string? _serialKey;
    [ObservableProperty] private string? _issuedSummary;

    [ObservableProperty] private bool _overrideTier;
    [ObservableProperty] private LicenseTier _overrideTierValue = LicenseTier.Professional;
    [ObservableProperty] private bool _overrideExpiry;
    [ObservableProperty] private DateTime? _overrideExpiryDate = DateTime.Today.AddYears(1);

    public ObservableCollection<string> Problems { get; } = new();

    public bool HasPreview => Preview is not null;
    public bool HasSerialKey => !string.IsNullOrEmpty(SerialKey);

    partial void OnPreviewChanged(SoftCodePreview? value) => OnPropertyChanged(nameof(HasPreview));
    partial void OnSerialKeyChanged(string? value) => OnPropertyChanged(nameof(HasSerialKey));

    partial void OnSoftCodeTextChanged(string value)
    {
        // A new request invalidates the previous decode and key.
        Preview = null;
        SerialKey = null;
        IssuedSummary = null;
        Problems.Clear();
    }

    [RelayCommand]
    private async Task Decode()
    {
        Error = null;
        SerialKey = null;
        Problems.Clear();

        var (preview, error) = await _activation.PreviewAsync(SoftCodeText);
        Preview = preview;
        Error = error;
        if (preview is not null)
            foreach (var problem in preview.Problems) Problems.Add(problem);
    }

    [RelayCommand]
    private async Task Generate()
    {
        Error = null;
        try
        {
            var overrides = new SerialKeyOverrides(
                OverrideTier ? OverrideTierValue : null,
                OverrideExpiry && OverrideExpiryDate is { } d ? DateOnly.FromDateTime(d) : null);

            var result = await _activation.IssueAsync(SoftCodeText, overrides);
            if (!result.Succeeded)
            {
                Error = result.Error;
                return;
            }

            SerialKey = result.SerialKey;
            var t = result.Terms!;
            IssuedSummary = $"{t.Tier} · {t.Type} · " +
                            (t.ExpiryDate is { } e ? $"expires {e:d}" : "no expiry") + " · " +
                            (t.MaxCardsPerPeriod is { } m ? $"{m:N0} cards per {t.LimitPeriod.ToString().ToLowerInvariant()[..^2]}" : "unlimited cards");
        }
        catch (Exception ex)
        {
            Log.Error("GenerateSerialKey: generate failed", ex);
            Error = ex.GetBaseException().Message;
        }
    }
}
