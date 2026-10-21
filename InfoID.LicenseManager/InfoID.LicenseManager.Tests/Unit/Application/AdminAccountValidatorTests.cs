using InfoID.LicenseManager.Application.Validators;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class AdminAccountValidatorTests
{
    [Theory]
    [InlineData("Strong1Password", null)]
    [InlineData("short1A", "Password must be at least 10 characters.")]
    [InlineData("alllowercase123", "Password must contain upper-case, lower-case letters and a digit.")]
    [InlineData("ALLUPPERCASE123", "Password must contain upper-case, lower-case letters and a digit.")]
    [InlineData("NoDigitsInHereAtAll", "Password must contain upper-case, lower-case letters and a digit.")]
    [InlineData("", "Password must be at least 10 characters.")]
    [InlineData(null, "Password must be at least 10 characters.")]
    public void Password_policy_matches_the_desktop_manager(string? password, string? expected) =>
        Assert.Equal(expected, AdminAccountValidator.ValidatePassword(password));

    [Fact]
    public void Absurdly_long_passwords_are_rejected()
    {
        var huge = "Aa1" + new string('x', AdminAccountValidator.MaxPasswordLength);

        Assert.NotNull(AdminAccountValidator.ValidatePassword(huge));
    }

    [Theory]
    [InlineData("a@b.com", true)]
    [InlineData("first.last@company.example", true)]
    [InlineData("no-at-sign", false)]
    [InlineData("@leading.com", false)]
    [InlineData("trailing@", false)]
    [InlineData("two@@signs.com", false)]
    [InlineData("white space@x.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Email_must_look_like_an_address(string? email, bool valid) =>
        Assert.Equal(valid, AdminAccountValidator.ValidateEmail(email) is null);

    [Theory]
    [InlineData("Jane Doe", true)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Name_is_required(string? name, bool valid) =>
        Assert.Equal(valid, AdminAccountValidator.ValidateName(name) is null);
}
