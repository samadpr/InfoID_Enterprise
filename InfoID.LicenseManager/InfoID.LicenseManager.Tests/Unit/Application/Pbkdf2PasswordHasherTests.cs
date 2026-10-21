using InfoID.LicenseManager.Infrastructure.Security;
using LegacyHasher = InfoID.Licensing.Server.Services.PasswordHasher;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class Pbkdf2PasswordHasherTests
{
    private readonly Pbkdf2PasswordHasher _hasher = new();

    [Fact]
    public void A_hash_verifies_only_for_its_own_password()
    {
        var stored = _hasher.Hash("Correct-Horse-9");

        Assert.True(_hasher.Verify("Correct-Horse-9", stored));
        Assert.False(_hasher.Verify("correct-horse-9", stored));
        Assert.False(_hasher.Verify("", stored));
    }

    [Fact]
    public void Hashes_use_the_existing_stored_format_with_a_fresh_salt_each_time()
    {
        var first = _hasher.Hash("Same-Password-1");
        var second = _hasher.Hash("Same-Password-1");

        var parts = first.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("PBKDF2", parts[0]);
        Assert.Equal("210000", parts[1]);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Hashes_written_by_the_legacy_license_server_still_verify()
    {
        var legacyStored = LegacyHasher.Hash("Legacy-Pass-123");

        Assert.True(_hasher.Verify("Legacy-Pass-123", legacyStored));
        Assert.False(_hasher.Verify("Legacy-Pass-124", legacyStored));
    }

    [Fact]
    public void Hashes_written_here_verify_in_the_legacy_license_server()
    {
        var stored = _hasher.Hash("Forward-Compat-1");

        Assert.True(LegacyHasher.Verify("Forward-Compat-1", stored));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plaintext")]
    [InlineData("PBKDF2$abc$AAAA$AAAA")]
    [InlineData("PBKDF2$1000$not base64!$AAAA")]
    [InlineData("OTHER$1000$AAAA$AAAA")]
    [InlineData("PBKDF2$1000$AAAA")]
    public void Malformed_stored_hashes_never_verify_and_never_throw(string stored) =>
        Assert.False(_hasher.Verify("anything", stored));

    [Fact]
    public void Simulated_verification_does_not_throw()
    {
        _hasher.SimulateVerify("whatever");
    }
}
