namespace InfoID.LicenseManager.Application.DTOs.Auth;

public sealed record SignInRequestDto(string Email, string Password);

public sealed record CreateInitialAdminRequestDto(string FullName, string Email, string Password);

/// <summary>The identity a successful sign-in produces. Carries no password material.</summary>
public sealed record AuthenticatedAdminDto(long Id, string FullName, string Email, string Role);
