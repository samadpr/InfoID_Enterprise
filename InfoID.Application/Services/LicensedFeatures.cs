using InfoID.Domain.Common.Enums;

namespace InfoID.Application.Services;

/// <summary>
/// Feature names passed to ILicenseService.CheckFeatureAllowedAsync, with the minimum
/// tier each needs (SRS 3.3 tier table). Only features that are actually gated in code
/// are listed as gated today; the others are defined so modules can adopt them.
/// </summary>
public static class LicensedFeatures
{
    // Standard
    public const string Printing = "Printing";
    public const string CardIssuance = "CardIssuance";
    /// <summary>Printing visitor badges (Visitor Management). Needs a valid license but
    /// does not use up the card allowance -- badges are temporary (agreed Oct 2026).</summary>
    public const string VisitorBadges = "VisitorBadges";

    // Professional
    public const string QrBarcode = "QrBarcode";
    public const string DatabaseImport = "DatabaseImport";
    public const string RfidMifare = "RfidMifare";
    public const string PrintQueue = "PrintQueue";

    // Enterprise
    public const string RfidAdvanced = "RfidAdvanced";
    public const string AccessControl = "AccessControl";
    public const string AdvancedReports = "AdvancedReports";
    public const string RestApi = "RestApi";
    public const string CloudBackup = "CloudBackup";

    /// <summary>Features that consume the card issuance allowance (FR-LIC-12).</summary>
    public static bool CountsCards(string feature) => feature is Printing or CardIssuance;

    public static LicenseTier MinimumTier(string feature) => feature switch
    {
        QrBarcode or DatabaseImport or RfidMifare or PrintQueue => LicenseTier.Professional,
        RfidAdvanced or AccessControl or AdvancedReports or RestApi or CloudBackup => LicenseTier.Enterprise,
        _ => LicenseTier.Standard,
    };
}
