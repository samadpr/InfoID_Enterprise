namespace InfoID.LicenseManager.Application.Interfaces.Security;

public interface IPasswordHasher
{
    /// <summary>Hashes a password in the stored format "PBKDF2$iterations$salt$hash".</summary>
    string Hash(string password);

    bool Verify(string password, string storedHash);

    /// <summary>Spends the same time as <see cref="Verify"/> without a real account, so an
    /// unknown e-mail cannot be told apart from a wrong password by response time.</summary>
    void SimulateVerify(string password);
}
