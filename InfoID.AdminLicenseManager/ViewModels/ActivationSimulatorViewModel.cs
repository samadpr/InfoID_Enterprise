using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Licensing.Core;
using InfoID.Licensing.Server.Services;

namespace InfoID.AdminLicenseManager.ViewModels;

/// <summary>
/// TEST TOOL (Super Admin only): plays the customer's side of offline activation so the
/// whole flow can be tested before InfoID.Desktop's activation screen exists. It uses
/// exactly the same InfoID.Licensing.Core calls InfoID.Desktop will use:
///   activation code + machine fingerprint -> request code (SoftCode.Create)
///   serial key -> SerialKey.Validate on "this machine".
/// The machine is simulated by a name, so changing the name simulates a different PC.
/// It does not write anything to the database.
/// </summary>
public sealed partial class ActivationSimulatorViewModel : ViewModelBase
{
    private readonly LicenseSecretStore _secrets;

    public ActivationSimulatorViewModel(LicenseSecretStore secrets) => _secrets = secrets;

    [ObservableProperty] private string _activationCodeText = "";
    [ObservableProperty] private string _machineName = "TEST-PC-1";
    [ObservableProperty] private string? _requestCode;
    [ObservableProperty] private string? _fingerprintHex;
    [ObservableProperty] private string _serialKeyText = "";
    [ObservableProperty] private string? _result;
    [ObservableProperty] private bool _resultIsError;

    public bool HasRequestCode => !string.IsNullOrEmpty(RequestCode);

    partial void OnRequestCodeChanged(string? value) => OnPropertyChanged(nameof(HasRequestCode));

    [RelayCommand]
    private void CreateRequestCode()
    {
        Result = null;
        RequestCode = null;
        FingerprintHex = null;

        if (!ActivationCode.TryParse(ActivationCodeText, out var code))
        {
            Show("That activation code isn't valid. InfoID would reject it before creating a request (typo check).", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(MachineName))
        {
            Show("Enter a machine name to simulate a computer.", error: true);
            return;
        }

        var fingerprint = SimulatedFingerprint();
        FingerprintHex = fingerprint.Hex;
        RequestCode = SoftCode.Create(code!, fingerprint).Encode();
    }

    [RelayCommand]
    private void VerifySerialKey()
    {
        if (!ActivationCode.TryParse(ActivationCodeText, out var code))
        {
            Show("Enter the activation code first (the same one used for the request).", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(MachineName))
        {
            Show("Enter the machine name.", error: true);
            return;
        }
        if (!_secrets.IsConfigured)
        {
            Show("No license secret is configured on this computer, so keys can't be checked. Set INFOID_LICENSE_SECRET_V1 and restart.", error: true);
            return;
        }

        var result = SerialKey.Validate(SerialKeyText, code!, SimulatedFingerprint(), _secrets.All);
        if (!result.IsValid)
        {
            Show("InfoID would REJECT this key: " + result.Error, error: true);
            return;
        }

        var t = result.Terms!;
        Show($"InfoID would UNLOCK on {MachineName.Trim()}: {t.Tier}, {t.Type}, " +
             (t.ExpiryDate is { } e ? $"expires {e:d}, " : "no expiry, ") +
             (t.MaxCardsPerPeriod is { } m ? $"{m:N0} cards per {t.LimitPeriod}." : "unlimited cards."),
             error: false);
    }

    private MachineFingerprint SimulatedFingerprint() =>
        MachineFingerprint.FromComponents(new Dictionary<string, string?> { ["SimulatedMachine"] = MachineName });

    private void Show(string message, bool error)
    {
        Result = message;
        ResultIsError = error;
    }
}
