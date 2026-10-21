using InfoID.Licensing.Core;

namespace InfoID.LicenseManager.Application.Common;

public static class ActivationCodeFormat
{
    /// <summary>XXXX-XXXX-XXXX-XXXX for a stored (normalized) code; anything unparseable is returned as stored.</summary>
    public static string Display(string normalized) =>
        ActivationCode.TryParse(normalized, out var parsed) ? parsed!.ToString() : normalized;
}
