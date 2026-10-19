using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Dtos.PrintingModule;
using InfoID.Domain.Common.Enums;

namespace InfoID.Desktop.Features.Printing.ViewModels;

/// <summary>What the operator chose when some people in a print run already have a card.</summary>
public enum ReprintChoice { Cancel, ReprintAll, SkipExisting }

public sealed record ReprintDecision(ReprintChoice Choice, ReprintReasonCode Reason, string? Note);

public sealed record ReprintReasonOption(ReprintReasonCode Code, string Label);

/// <summary>
/// Inline prompt shown over the Print dialog (FR-PRN-4) when people in the run already
/// have an issued card for this design: lists them, asks for the reprint reason (Lost,
/// Damaged, Renewed, Other + note), and lets the operator reprint, print only the new
/// people, or cancel. Shown inside the Print window rather than as a second window so it
/// can never open behind it.
/// </summary>
public sealed partial class ReprintPromptViewModel : ObservableObject
{
    private readonly TaskCompletionSource<ReprintDecision> _completion = new();

    public IReadOnlyList<ExistingCardDto> ExistingCards { get; }
    public int ExistingCount => ExistingCards.Count;
    public int NewCount { get; }
    public bool HasNewPeople => NewCount > 0;

    public string Heading => ExistingCount == 1
        ? "1 person already has a card"
        : $"{ExistingCount} people already have a card";

    public string Explanation => NewCount == 0
        ? "Every record in this run was printed before with this design. Printing again replaces their current card; choose why."
        : $"{NewCount} record{(NewCount == 1 ? " is" : "s are")} new. The people below were printed before with this design; printing them again replaces their current card.";

    public string SkipText => $"Print {NewCount} new only";

    /// <summary>Explains the expiry rule for the selected reason.</summary>
    public string ExpiryNote => SelectedReason.Code is ReprintReasonCode.Lost or ReprintReasonCode.Damaged
        ? "Replacement cards keep the original card's expiry date."
        : "Replacement cards get a new expiry date from the \"Card valid for\" setting.";

    partial void OnSelectedReasonChanged(ReprintReasonOption value) => OnPropertyChanged(nameof(ExpiryNote));

    public IReadOnlyList<ReprintReasonOption> Reasons { get; } = new[]
    {
        new ReprintReasonOption(ReprintReasonCode.Lost, "Lost"),
        new ReprintReasonOption(ReprintReasonCode.Damaged, "Damaged"),
        new ReprintReasonOption(ReprintReasonCode.Renewed, "Renewed / details changed"),
        new ReprintReasonOption(ReprintReasonCode.Other, "Other"),
    };

    [ObservableProperty]
    private ReprintReasonOption _selectedReason = null!;

    [ObservableProperty]
    private string? _note;

    public ReprintPromptViewModel(IReadOnlyList<ExistingCardDto> existingCards, int newCount)
    {
        ExistingCards = existingCards;
        NewCount = newCount;
        SelectedReason = Reasons.First(r => r.Code == ReprintReasonCode.Damaged);
    }

    public Task<ReprintDecision> Completion => _completion.Task;

    [RelayCommand]
    private void ReprintAll() => Finish(ReprintChoice.ReprintAll);

    [RelayCommand]
    private void SkipExisting() => Finish(ReprintChoice.SkipExisting);

    [RelayCommand]
    private void Cancel() => Finish(ReprintChoice.Cancel);

    private void Finish(ReprintChoice choice) =>
        _completion.TrySetResult(new ReprintDecision(
            choice,
            SelectedReason.Code,
            string.IsNullOrWhiteSpace(Note) ? null : Note.Trim()));
}
