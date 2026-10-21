namespace InfoID.LicenseManager.Web.Security;

public static class RateLimitPolicies
{
    /// <summary>Staff sign-in and first-run setup: 10 a minute per address.</summary>
    public const string Login = "login";

    /// <summary>Activation, check-in, deactivation: activation codes cannot be guessed at scale (20 a minute per address).</summary>
    public const string Api = "api";

    /// <summary>Sign-up, login, codes, plans and requests: tighter, against guessing and spam (15 a minute per address).</summary>
    public const string Account = "account";

    /// <summary>Trial printing: several calls per print job, possibly from many PCs behind one office address (60 a minute).</summary>
    public const string Trial = "trial";

    /// <summary>The phone/QR activation page (10 a minute per address).</summary>
    public const string Phone = "phone";
}
