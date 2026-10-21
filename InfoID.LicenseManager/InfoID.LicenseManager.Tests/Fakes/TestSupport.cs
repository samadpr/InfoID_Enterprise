using InfoID.LicenseManager.Application.Common;
using InfoID.LicenseManager.Application.Interfaces.Security;

namespace InfoID.LicenseManager.Tests.Fakes;

/// <summary>A clock the test moves by hand.</summary>
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now;

    public TestClock(DateTimeOffset? start = null) => _now = start ?? new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public sealed class FakeCurrentUser : ICurrentUser
{
    public bool IsAuthenticated { get; set; }
    public long? AdminUserId { get; set; }
    public string? Email { get; set; }
    public string? Role { get; set; }
    public string? ClientAddress { get; set; } = "127.0.0.1";
}

/// <summary>Instant, reversible "hashing" so service tests do not pay for 210,000 PBKDF2 rounds each.
/// The real hasher has its own tests.</summary>
public sealed class FakePasswordHasher : IPasswordHasher
{
    public int SimulatedVerifications { get; private set; }

    public string Hash(string password) => "fake$" + password;

    public bool Verify(string password, string storedHash) => storedHash == "fake$" + password;

    public void SimulateVerify(string password) => SimulatedVerifications++;
}
