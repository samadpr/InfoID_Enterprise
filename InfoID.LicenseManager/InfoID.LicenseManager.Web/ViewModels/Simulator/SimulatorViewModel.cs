using System.ComponentModel.DataAnnotations;
using InfoID.LicenseManager.Application.DTOs.Activations;

namespace InfoID.LicenseManager.Web.ViewModels.Simulator;

public sealed class SimulatorViewModel
{
    [Required(ErrorMessage = "Enter the activation code.")]
    [StringLength(60)]
    [Display(Name = "Activation code")]
    public string ActivationCode { get; set; } = "";

    [Required(ErrorMessage = "Enter a machine name.")]
    [StringLength(100)]
    [Display(Name = "Machine name")]
    public string MachineName { get; set; } = "TEST-PC-1";

    [StringLength(100)]
    [Display(Name = "Serial key")]
    public string SerialKey { get; set; } = "";

    public bool SecretConfigured { get; set; }

    public SimulatedRequestCodeDto? Request { get; set; }

    public SimulatedVerificationDto? Verification { get; set; }

    public string? Error { get; set; }
}
