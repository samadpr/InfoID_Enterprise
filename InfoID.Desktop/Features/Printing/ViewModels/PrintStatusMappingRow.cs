using CommunityToolkit.Mvvm.ComponentModel;
using InfoID.Desktop.Features.Printing.Models;

namespace InfoID.Desktop.Features.Printing.ViewModels;

/// <summary>One row of the Advanced Print Operations "Automatic Print Status Update"
/// list -- which sample field to read (stands in for a database column until the real
/// Cardholder Management module exists) and which marker it drives after a successful
/// print. See PrintStatusMarker for what each marker actually does.</summary>
public sealed partial class PrintStatusMappingRow : ObservableObject
{
    [ObservableProperty]
    private string? _sourceColumn;

    [ObservableProperty]
    private PrintStatusMarker _marker = PrintStatusMarker.PrintStatus;
}
