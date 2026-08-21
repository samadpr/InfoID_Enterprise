using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Models;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.BlankCard.Models;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.Templates.Models;
using InfoID.Desktop.Features.Templates.Services;
using InfoID.Desktop.ViewModels.Base;

namespace InfoID.Desktop.Features.Templates.ViewModels;

/// <summary>
/// Template browser: category filter chips plus orientation/format dropdown filters,
/// exactly mirroring the Blank Card page's filter pattern for a consistent feel. Backed
/// entirely by <see cref="ITemplateCatalogService"/>.
/// </summary>
public sealed partial class TemplatesViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly ITemplateCatalogService _catalogService;

    private IReadOnlyList<TemplateCatalogItem> _allTemplates = Array.Empty<TemplateCatalogItem>();

    public TemplatesViewModel(INavigationService navigationService, ITemplateCatalogService catalogService)
    {
        _navigationService = navigationService;
        _catalogService = catalogService;
    }

    public ObservableCollection<string> OrientationOptions { get; } = new() { "All", "Landscape", "Portrait" };

    public ObservableCollection<string> FormatOptions { get; } = new() { "All", "Access", "Business" };

    public ObservableCollection<FilterChipOption> CategoryChips { get; } = new();

    public ObservableCollection<TemplateCatalogItem> FilteredTemplates { get; } = new();

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
        _allTemplates = await _catalogService.GetTemplatesAsync();
        var categories = await _catalogService.GetCategoriesAsync();

        CategoryChips.Clear();
        CategoryChips.Add(new FilterChipOption("All", isSelected: true, onChanged: () => OnChipToggled(null)));
        foreach (var category in categories)
        {
            CategoryChips.Add(new FilterChipOption(category, onChanged: () => OnChipToggled(category)));
        }

        ApplyFilters();
    }

    private bool _suppressChipSync;

    private void OnChipToggled(string? category)
    {
        if (_suppressChipSync)
        {
            return;
        }

        _suppressChipSync = true;
        foreach (var chip in CategoryChips)
        {
            chip.IsSelected = category is null
                ? chip.Label == "All"
                : chip.Label == category;
        }
        _suppressChipSync = false;

        ApplyFilters();
    }

    partial void OnSelectedOrientationOptionChanged(string value) => ApplyFilters();

    partial void OnSelectedFormatOptionChanged(string value) => ApplyFilters();

    private void ApplyFilters()
    {
        var selectedCategory = CategoryChips.FirstOrDefault(c => c.IsSelected)?.Label ?? "All";

        var query = _allTemplates.AsEnumerable();

        if (selectedCategory != "All")
        {
            query = query.Where(t => t.Category == selectedCategory);
        }

        if (SelectedOrientationOption != "All")
        {
            var orientation = SelectedOrientationOption == "Landscape"
                ? CardOrientation.Landscape
                : CardOrientation.Portrait;
            query = query.Where(t => t.Orientation == orientation);
        }

        if (SelectedFormatOption != "All")
        {
            query = query.Where(t => t.Format == SelectedFormatOption);
        }

        FilteredTemplates.Clear();
        foreach (var template in query)
        {
            FilteredTemplates.Add(template);
        }
    }

    [RelayCommand]
    private void GoBack() => _navigationService.GoBack();

    [RelayCommand]
    private void UseTemplate(TemplateCatalogItem template) =>
        _navigationService.NavigateTo<CardDesignerViewModel>(template);
}
