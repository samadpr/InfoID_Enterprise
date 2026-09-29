using System.Security.Cryptography;

namespace InfoID.Licensing.Server.Services;

/// <summary>PBKDF2-SHA256 password hashing (BCL only). Stored as
/// "PBKDF2$iterations$saltBase64$hashBase64" so iterations can be raised later
/// without breaking existing accounts.</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2" || !int.TryParse(parts[1], out var iterations))
            return false;

        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Minimum policy for staff accounts on a licensing system.</summary>
    public static string? ValidateStrength(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 10)
            return "Password must be at least 10 characters.";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            return "Password must contain upper-case, lower-case letters and a digit.";
        return null;
    }
}
