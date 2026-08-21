using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Models;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.BlankCard.Models;
using InfoID.Desktop.Features.BlankCard.Services;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.ViewModels.Base;

namespace InfoID.Desktop.Features.BlankCard.ViewModels;

/// <summary>
/// Blank Card selection page: orientation/format dropdown filters, a scalable set of
/// type filter chips (driven entirely by <see cref="IBlankCardCatalogService"/>, never
/// hardcoded here) and the "Create my own card" flow.
/// </summary>
public sealed partial class BlankCardViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IDialogService _dialogService;
    private readonly IBlankCardCatalogService _catalogService;

    private IReadOnlyList<CardFormatOption> _allFormats = Array.Empty<CardFormatOption>();

    public BlankCardViewModel(
        INavigationService navigationService,
        IDialogService dialogService,
        IBlankCardCatalogService catalogService)
    {
        _navigationService = navigationService;
        _dialogService = dialogService;
        _catalogService = catalogService;
    }

    public ObservableCollection<string> OrientationOptions { get; } = new() { "All", "Landscape", "Portrait" };

    public ObservableCollection<string> FormatOptions { get; } = new() { "All", "CR-80" };

    public ObservableCollection<FilterChipOption> TypeChips { get; } = new();

    public ObservableCollection<CardFormatOption> FilteredFormats { get; } = new();

    [ObservableProperty]
    private string _selectedOrientationOption = "All";

    [ObservableProperty]
    private string _selectedFormatOption = "All";

    public override void OnNavigatedTo(object? parameter)
    {
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _allFormats = await _catalogService.GetFormatsAsync();
        var tags = await _catalogService.GetFilterTagsAsync();

        TypeChips.Clear();
        TypeChips.Add(new FilterChipOption("All", isSelected: true, onChanged: () => OnChipToggled(null)));
        foreach (var tag in tags)
        {
            TypeChips.Add(new FilterChipOption(tag, onChanged: () => OnChipToggled(tag)));
        }

        ApplyFilters();
    }

    private bool _suppressChipSync;

    private void OnChipToggled(string? tag)
    {
        if (_suppressChipSync)
        {
            return;
        }

        _suppressChipSync = true;
        foreach (var chip in TypeChips)
        {
            chip.IsSelected = tag is null
                ? chip.Label == "All"
                : chip.Label == tag;
        }
        _suppressChipSync = false;

        ApplyFilters();
    }

    partial void OnSelectedOrientationOptionChanged(string value) => ApplyFilters();

    partial void OnSelectedFormatOptionChanged(string value) => ApplyFilters();

    private void ApplyFilters()
    {
        var selectedTag = TypeChips.FirstOrDefault(c => c.IsSelected)?.Label ?? "All";

        var query = _allFormats.AsEnumerable();

        if (selectedTag != "All")
        {
            query = query.Where(f => f.Tags.Contains(selectedTag));
        }

        if (SelectedOrientationOption != "All")
        {
            var orientation = SelectedOrientationOption == "Landscape"
                ? CardOrientation.Landscape
                : CardOrientation.Portrait;
            query = query.Where(f => f.Orientation == orientation);
        }

        if (SelectedFormatOption != "All")
        {
            query = query.Where(f => f.CardSizeName == SelectedFormatOption);
        }

        FilteredFormats.Clear();
        foreach (var format in query)
        {
            FilteredFormats.Add(format);
        }
    }

    [RelayCommand]
    private void GoBack() => _navigationService.GoBack();

    [RelayCommand]
    private void SelectFormat(CardFormatOption format) =>
        _navigationService.NavigateTo<CardDesignerViewModel>(format);

    [RelayCommand]
    private async Task CreateMyOwnCardAsync()
    {
        var dialogViewModel = new CustomCardDialogViewModel();
        var result = await _dialogService.ShowDialogAsync<CustomCardDialogViewModel, CustomCardSizeResult>(dialogViewModel);

        if (result is not null)
        {
            _navigationService.NavigateTo<CardDesignerViewModel>(result);
        }
    }
}
