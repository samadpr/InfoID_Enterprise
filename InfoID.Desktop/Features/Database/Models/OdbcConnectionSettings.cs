namespace InfoID.Desktop.Features.Database.Models;

public sealed class OdbcConnectionSettings
{
    public string Dsn { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool UseWindowsAuthentication { get; set; }
}
