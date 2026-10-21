using System.Security.Cryptography;
using InfoID.LicenseManager.Application.Interfaces.Security;

namespace InfoID.LicenseManager.Infrastructure.Security;

/// <summary>
/// PBKDF2-SHA256 password hashing (BCL only), stored as
/// "PBKDF2$iterations$saltBase64$hashBase64". This is exactly the format InfoID.Licensing.Server
/// writes, so existing staff accounts keep working in both directions; the iteration count is
/// read from each stored hash, so it can be raised later without invalidating accounts.
/// </summary>
public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    // Computed once: used only to spend verification time when the account does not exist.
    private static readonly Lazy<string> DummyHash = new(() => HashCore("timing-equalisation-only"));

    public string Hash(string password) => HashCore(password);

    public bool Verify(string password, string storedHash)
    {
        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2" || !int.TryParse(parts[1], out var iterations) || iterations < 1)
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void SimulateVerify(string password) => Verify(password, DummyHash.Value);

    private static string HashCore(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
}
