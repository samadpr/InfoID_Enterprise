using System.ComponentModel.DataAnnotations;
using InfoID.LicenseManager.Application.DTOs.Customers;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace InfoID.LicenseManager.Web.ViewModels.Customers;

public sealed class CustomerListQuery
{
    public CustomerFilter Filter { get; set; } = CustomerFilter.All;

    [StringLength(200)]
    public string? Text { get; set; }
}

public sealed class CustomerListViewModel
{
    public CustomerListViewModel(CustomerListQuery query, IReadOnlyList<CustomerListItemDto> rows)
    {
        Query = query;
        Rows = rows;
    }

    public CustomerListQuery Query { get; }

    public IReadOnlyList<CustomerListItemDto> Rows { get; }

    public static IReadOnlyList<SelectListItem> FilterOptions { get; } =
    [
        new("All", nameof(CustomerFilter.All)),
        new("Leads (no requests yet)", nameof(CustomerFilter.Leads)),
        new("Verified", nameof(CustomerFilter.Verified)),
        new("Not verified", nameof(CustomerFilter.NotVerified)),
        new("Disabled", nameof(CustomerFilter.Disabled)),
    ];

    public static string StatusText(CustomerStatus status) => status switch
    {
        CustomerStatus.Disabled => "Disabled",
        CustomerStatus.NotVerified => "Not verified",
        CustomerStatus.Lead => "Lead",
        _ => "Customer",
    };
}

public sealed class DisableCustomerFormViewModel
{
    [Required(ErrorMessage = "Enter a reason for disabling the account.")]
    [StringLength(300)]
    public string Reason { get; set; } = "";
}

public sealed class CustomerDetailsViewModel
{
    public CustomerDetailsViewModel(CustomerDetailsDto details) => Details = details;

    public CustomerDetailsDto Details { get; }

    public DisableCustomerFormViewModel Disable { get; } = new();
}
