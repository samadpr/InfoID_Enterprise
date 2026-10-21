using InfoID.LicenseManager.Application.DTOs.Auth;

namespace InfoID.LicenseManager.Application.Validators;

/// <summary>Input rules for staff accounts. Returns the first problem as a user-facing message, or null.</summary>
public static class AdminAccountValidator
{
    public const int MinPasswordLength = 10;
    public const int MaxPasswordLength = 128;

    public static string? Validate(CreateInitialAdminRequestDto request) =>
        ValidateName(request.FullName) ?? ValidateEmail(request.Email) ?? ValidatePassword(request.Password);

    public static string? ValidateName(string? fullName) =>
        string.IsNullOrWhiteSpace(fullName) ? "Enter your full name."
        : fullName.Trim().Length > 200 ? "The name is too long."
        : null;

    public static string? ValidateEmail(string? email)
    {
        email = email?.Trim();
        if (string.IsNullOrWhiteSpace(email) || email.Length > 200) return "Enter a valid email address.";
        var at = email.IndexOf('@');
        return at < 1 || at != email.LastIndexOf('@') || at == email.Length - 1 || email.Any(char.IsWhiteSpace)
            ? "Enter a valid email address."
            : null;
    }

    /// <summary>Minimum policy for staff accounts on a licensing system (same as the desktop manager).
    /// The upper bound keeps PBKDF2 from being used to burn CPU with huge inputs.</summary>
    public static string? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            return $"Password must be at least {MinPasswordLength} characters.";
        if (password.Length > MaxPasswordLength)
            return $"Password must be at most {MaxPasswordLength} characters.";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            return "Password must contain upper-case, lower-case letters and a digit.";
        return null;
    }
}
