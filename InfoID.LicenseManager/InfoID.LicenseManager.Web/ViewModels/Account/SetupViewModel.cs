using System.ComponentModel.DataAnnotations;

namespace InfoID.LicenseManager.Web.ViewModels.Account;

/// <summary>First-run form that creates the initial Super Admin. The role is not a field:
/// nothing the browser sends can choose it.</summary>
public sealed class SetupViewModel
{
    [Required(ErrorMessage = "Enter your full name.")]
    [StringLength(200)]
    [Display(Name = "Full name")]
    public string FullName { get; set; } = "";

    [Required(ErrorMessage = "Enter your email address.")]
    [StringLength(200)]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Choose a password.")]
    [DataType(DataType.Password)]
    [StringLength(1024)]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Repeat the password.")]
    [DataType(DataType.Password)]
    [Compare(nameof(Password), ErrorMessage = "The passwords do not match.")]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = "";
}
