namespace InfoID.LicenseManager.Web.Security;

/// <summary>Authorization policy names. The rules themselves are the Domain's AdminRoles methods.</summary>
public static class AuthPolicies
{
    /// <summary>Create, edit, revoke and extend licenses (Super Admin, License Manager).</summary>
    public const string ManageLicenses = nameof(ManageLicenses);

    public const string SuperAdmin = nameof(SuperAdmin);
}
