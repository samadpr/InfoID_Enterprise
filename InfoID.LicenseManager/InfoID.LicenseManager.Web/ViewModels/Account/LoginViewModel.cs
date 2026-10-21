using System.ComponentModel.DataAnnotations;

namespace InfoID.LicenseManager.Web.ViewModels.Account;

public sealed class LoginViewModel
{
    [Required(ErrorMessage = "Enter your email address.")]
    [StringLength(200)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Enter your password.")]
    [DataType(DataType.Password)]
    [StringLength(1024)]
    public string Password { get; set; } = "";

    /// <summary>Where to go after signing in; only ever used when it is a local URL.</summary>
    public string? ReturnUrl { get; set; }
}
