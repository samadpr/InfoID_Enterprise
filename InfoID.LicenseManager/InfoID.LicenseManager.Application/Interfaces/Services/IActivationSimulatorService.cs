using InfoID.LicenseManager.Application.DTOs.Activations;
using InfoID.Shared.Common;

namespace InfoID.LicenseManager.Application.Interfaces.Services;

/// <summary>
/// TEST TOOL (Super Admin only): plays the customer's side of offline activation with the same
/// InfoID.Licensing.Core calls InfoID.Desktop uses, so the whole flow can be tried end to end. The
/// machine is simulated by a name; a different name is a different PC. Writes nothing to the database.
/// </summary>
public interface IActivationSimulatorService
{
    bool IsSecretConfigured { get; }

    Result<SimulatedRequestCodeDto> CreateRequestCode(string activationCodeText, string machineName);

    Result<SimulatedVerificationDto> VerifySerialKey(string activationCodeText, string machineName, string serialKeyText);
}
