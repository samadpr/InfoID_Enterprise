using InfoID.Licensing.Core;
using InfoID.LicenseManager.Application.Interfaces.Security;

namespace InfoID.LicenseManager.Tests.Fakes;

/// <summary>Fixed secrets so serial keys are reproducible in tests.</summary>
public sealed class TestSecretProvider : ILicenseSecretProvider
{
    public static readonly LicenseSecret V1 = new(1, Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
    public static readonly LicenseSecret V2 = new(2, Enumerable.Range(101, 32).Select(i => (byte)i).ToArray());

    private readonly IReadOnlyList<LicenseSecret> _secrets;

    public TestSecretProvider(params LicenseSecret[] secrets) => _secrets = secrets.Length == 0 ? [V1] : secrets;

    public static TestSecretProvider None => new(Array.Empty<LicenseSecret>()) { Empty = true };

    private bool Empty { get; init; }

    public bool IsConfigured => !Empty;

    public IReadOnlyList<LicenseSecret> All => Empty ? [] : _secrets;

    public LicenseSecret Current => Empty
        ? throw new InvalidOperationException("No license secret is configured.")
        : _secrets.MaxBy(s => s.Version)!;
}
