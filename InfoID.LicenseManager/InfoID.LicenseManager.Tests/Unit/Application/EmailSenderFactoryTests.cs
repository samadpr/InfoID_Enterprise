using System.Reflection;
using InfoID.LicenseManager.Infrastructure.ExternalServices;
using Microsoft.Extensions.Configuration;

namespace InfoID.LicenseManager.Tests.Unit.Application;

public class EmailSenderFactoryTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value))).Build();

    private static T Field<T>(object sender, string name) =>
        (T)sender.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sender)!;

    [Fact]
    public void Without_a_host_mail_goes_to_the_outbox_folder()
    {
        var sender = EmailSenderFactory.Create(Config());

        Assert.IsType<OutboxEmailSender>(sender);
        Assert.False(sender.IsRealDelivery);
    }

    [Fact]
    public void The_EmailSettings_section_configures_real_delivery()
    {
        var sender = EmailSenderFactory.Create(Config(
            ("EmailSettings:Host", " smtp.example.com "), ("EmailSettings:Port", "2525"), ("EmailSettings:Username", "robot@example.com"),
            ("EmailSettings:Password", "secret"), ("EmailSettings:From", "licenses@example.com"), ("EmailSettings:FromName", "InfoID Licenses"),
            ("EmailSettings:EnableSsl", "false")));

        Assert.IsType<SmtpEmailSender>(sender);
        Assert.True(sender.IsRealDelivery);
        Assert.Equal("smtp.example.com", Field<string>(sender, "_host"));
        Assert.Equal(2525, Field<int>(sender, "_port"));
        Assert.Equal("robot@example.com", Field<string>(sender, "_user"));
        Assert.Equal("licenses@example.com", Field<string>(sender, "_from"));
        Assert.Equal("InfoID Licenses", Field<string>(sender, "_fromName"));
        Assert.False(Field<bool>(sender, "_ssl"));
    }

    [Fact]
    public void Common_alternative_key_names_are_accepted_and_defaults_apply()
    {
        var sender = EmailSenderFactory.Create(Config(
            ("EmailSettings:SmtpServer", "smtp.example.com"), ("EmailSettings:UserName", "robot@example.com"), ("EmailSettings:Password", "secret")));

        Assert.Equal("smtp.example.com", Field<string>(sender, "_host"));
        Assert.Equal(587, Field<int>(sender, "_port"));
        Assert.Equal("robot@example.com", Field<string>(sender, "_from")); // the sign-in name is the sender when no From is given
        Assert.True(Field<bool>(sender, "_ssl"));
    }

    [Fact]
    public void A_password_without_a_user_name_signs_in_with_the_sender_address()
    {
        // The shape used by a Gmail mailbox: Email + Password + Host + DisplayName + Port.
        var sender = EmailSenderFactory.Create(Config(
            ("EmailSettings:Email", "sender@gmail.com"), ("EmailSettings:Password", "app password"), ("EmailSettings:Host", "smtp.gmail.com"),
            ("EmailSettings:DisplayName", "InfoID"), ("EmailSettings:Port", "587")));

        Assert.Equal("sender@gmail.com", Field<string>(sender, "_user"));
        Assert.Equal("sender@gmail.com", Field<string>(sender, "_from"));
        Assert.Equal("InfoID", Field<string>(sender, "_fromName"));
        Assert.Equal("app password", Field<string?>(sender, "_password"));
        Assert.True(Field<bool>(sender, "_ssl"));
    }

    [Fact]
    public void The_environment_variables_win_over_the_section()
    {
        var sender = EmailSenderFactory.Create(Config(
            ("EmailSettings:Host", "from-file.example.com"), ("INFOID_SMTP_HOST", "from-env.example.com"), ("INFOID_SMTP_PORT", "465")));

        Assert.Equal("from-env.example.com", Field<string>(sender, "_host"));
        Assert.Equal(465, Field<int>(sender, "_port"));
    }

    [Fact]
    public void A_blank_host_in_the_section_still_means_outbox()
    {
        Assert.IsType<OutboxEmailSender>(EmailSenderFactory.Create(Config(("EmailSettings:Host", "  "), ("EmailSettings:Password", "x"))));
    }
}
