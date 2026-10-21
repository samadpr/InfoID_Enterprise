using InfoID.LicenseManager.Infrastructure.Cryptography;
using Microsoft.Extensions.Configuration;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class ConfigurationLicenseSecretProviderTests
{
    private static string Secret(byte fill, int length = 32) => Convert.ToBase64String(Enumerable.Repeat(fill, length).ToArray());

    private static ConfigurationLicenseSecretProvider Provider(params (string Key, string Value)[] settings) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value)).Build());

    [Fact]
    public void Nothing_configured_means_not_configured_and_no_current_secret()
    {
        var provider = Provider();

        Assert.False(provider.IsConfigured);
        Assert.Empty(provider.All);
        var ex = Assert.Throws<InvalidOperationException>(() => provider.Current);
        Assert.Contains("INFOID_LICENSE_SECRET_V1", ex.Message);
    }

    [Fact]
    public void The_highest_version_is_current_and_every_version_stays_available_for_validation()
    {
        var provider = Provider(("INFOID_LICENSE_SECRET_V1", Secret(1)), ("INFOID_LICENSE_SECRET_V3", Secret(3)), ("INFOID_LICENSE_SECRET_V2", Secret(2)));

        Assert.True(provider.IsConfigured);
        Assert.Equal(3, provider.Current.Version);
        Assert.Equal([1, 2, 3], provider.All.Select(s => (int)s.Version).Order());
        Assert.Equal(Enumerable.Repeat((byte)3, 32), provider.Current.Key);
    }

    [Fact]
    public void Blank_entries_are_ignored()
    {
        var provider = Provider(("INFOID_LICENSE_SECRET_V1", "   "), ("INFOID_LICENSE_SECRET_V2", Secret(2)));

        Assert.Single(provider.All);
        Assert.Equal(2, provider.Current.Version);
    }

    [Fact]
    public void An_invalid_secret_fails_at_start_naming_the_setting_but_never_echoing_the_value()
    {
        const string notBase64 = "this-is-NOT-base64-and-secret!";

        var ex = Assert.Throws<InvalidOperationException>(() => Provider(("INFOID_LICENSE_SECRET_V1", notBase64)));

        Assert.Contains("INFOID_LICENSE_SECRET_V1", ex.Message);
        Assert.DoesNotContain(notBase64, ex.Message);
    }

    [Fact]
    public void A_secret_shorter_than_32_bytes_is_refused_without_echoing_it()
    {
        var shortSecret = Secret(9, 16);

        var ex = Assert.Throws<InvalidOperationException>(() => Provider(("INFOID_LICENSE_SECRET_V1", shortSecret)));

        Assert.Contains("32 bytes", ex.Message);
        Assert.DoesNotContain(shortSecret, ex.Message);
    }
}
