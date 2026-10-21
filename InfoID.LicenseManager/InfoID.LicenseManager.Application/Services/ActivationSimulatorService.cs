using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.DTOs.Activations;
using InfoID.LicenseManager.Application.Interfaces.Security;
using InfoID.LicenseManager.Application.Interfaces.Services;
using InfoID.Shared.Common;

namespace InfoID.LicenseManager.Application.Services;

public sealed class ActivationSimulatorService : IActivationSimulatorService
{
    private readonly ILicenseSecretProvider _secrets;
    private readonly ICurrentUser _currentUser;

    public ActivationSimulatorService(ILicenseSecretProvider secrets, ICurrentUser currentUser)
    {
        _secrets = secrets;
        _currentUser = currentUser;
    }

    public bool IsSecretConfigured => _secrets.IsConfigured;

    public Result<SimulatedRequestCodeDto> CreateRequestCode(string activationCodeText, string machineName)
    {
        _currentUser.RequireSuperAdmin("use the customer simulator");

        if (!ActivationCode.TryParse(activationCodeText, out var code))
            return Result<SimulatedRequestCodeDto>.Failure("That activation code isn't valid. InfoID would reject it before creating a request (typo check).");
        if (string.IsNullOrWhiteSpace(machineName))
            return Result<SimulatedRequestCodeDto>.Failure("Enter a machine name to simulate a computer.");

        var fingerprint = SimulatedFingerprint(machineName);
        return Result<SimulatedRequestCodeDto>.Success(new SimulatedRequestCodeDto(SoftCode.Create(code!, fingerprint).Encode(), fingerprint.Hex));
    }

    public Result<SimulatedVerificationDto> VerifySerialKey(string activationCodeText, string machineName, string serialKeyText)
    {
        _currentUser.RequireSuperAdmin("use the customer simulator");

        if (!ActivationCode.TryParse(activationCodeText, out var code))
            return Result<SimulatedVerificationDto>.Failure("Enter the activation code first (the same one used for the request).");
        if (string.IsNullOrWhiteSpace(machineName))
            return Result<SimulatedVerificationDto>.Failure("Enter the machine name.");
        if (!_secrets.IsConfigured)
            return Result<SimulatedVerificationDto>.Failure("No license secret is configured on the server, so keys can't be checked. Set INFOID_LICENSE_SECRET_V1 and restart.");

        var result = SerialKey.Validate(serialKeyText, code!, SimulatedFingerprint(machineName), _secrets.All);
        if (!result.IsValid)
            return Result<SimulatedVerificationDto>.Failure("InfoID would REJECT this key: " + result.Error);

        var terms = result.Terms!;
        return Result<SimulatedVerificationDto>.Success(new SimulatedVerificationDto(
            terms.Tier, terms.Type, terms.ExpiryDate, terms.MaxCardsPerPeriod, terms.LimitPeriod));
    }

    private static MachineFingerprint SimulatedFingerprint(string machineName) =>
        MachineFingerprint.FromComponents(new Dictionary<string, string?> { ["SimulatedMachine"] = machineName });
}
