using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.BlankCard.Models;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.Templates.Models;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Linq;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>
/// The Card Designer workspace. Hosts one or more open <see cref="CardDesignTabViewModel"/>
/// documents (Part 6 -- multi-document tabs), each with its own document, zoom/pan,
/// selection and undo/redo. Replaces the old single-static-preview placeholder.
/// </summary>
public sealed partial class CardDesignerViewModel : ViewModelBase
{
    private readonly INavigationService _navigationService;
    private readonly IDesignClipboard _clipboard;
    private readonly ICardDesignRepository _repository;
    private readonly IFilePickerService _filePicker;
    private readonly IDesignAssetService _assetService;
    private readonly IDataBindingEvaluator _evaluator;
    private readonly IPreviewDataProvider _previewData;
    private int _untitledCounter = 1;

    public CardDesignerViewModel(
        INavigationService navigationService, IDesignClipboard clipboard, ICardDesignRepository repository,
        IFilePickerService filePicker, IDesignAssetService assetService,
        IDataBindingEvaluator evaluator, IPreviewDataProvider previewData)
    {
        _navigationService = navigationService;
        _clipboard = clipboard;
        _repository = repository;
        _filePicker = filePicker;
        _assetService = assetService;
        _evaluator = evaluator;
        _previewData = previewData;
    }

    public ObservableCollection<CardDesignTabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private CardDesignTabViewModel? _activeTab;

    public override void OnNavigatedTo(object? parameter)
    {
        _ = OpenFromParameterAsync(parameter);
    }

    private async System.Threading.Tasks.Task OpenFromParameterAsync(object? parameter)
    {
        if (parameter is long templateId)
        {
            var document = await _repository.LoadAsync(templateId);
            if (document is not null)
            {
                await _repository.MarkOpenedAsync(templateId);
                OpenNewTab(document);
                return;
            }
        }

        var newDocument = BuildDocumentFromNavigationParameter(parameter);
        OpenNewTab(newDocument);
    }

    partial void OnActiveTabChanged(CardDesignTabViewModel? value)
    {
        foreach (var t in Tabs) t.IsActive = false;
        if (value is not null) value.IsActive = true;
    }

    private CardDesignDocument BuildDocumentFromNavigationParameter(object? parameter)
    {
        var document = new CardDesignDocument();

        switch (parameter)
        {
            case CardFormatOption format:
                document.Name = format.Name;
                document.WidthMm = format.WidthMm;
                document.HeightMm = format.HeightMm;
                document.Orientation = format.Orientation;
                break;

            case CustomCardSizeResult custom:
                document.Name = custom.ModelName is { Length: > 0 } ? custom.ModelName : "Custom card";
                document.WidthMm = custom.Width;
                document.HeightMm = custom.Height;
                document.Orientation = custom.Orientation;
                document.CornerRadiusMm = custom.CornerRadius;
                break;

            case TemplateCatalogItem template:
                document.Name = template.Name;
                document.Orientation = template.Orientation;
                break;

            default:
                document.Name = $"Untitled {_untitledCounter++}";
                break;
        }

        return document;
    }

    private void OpenNewTab(CardDesignDocument document)
    {
        var tab = new CardDesignTabViewModel(document, _clipboard, _repository, _filePicker, _assetService, _evaluator, _previewData);
        Tabs.Add(tab);
        ActiveTab = tab;
    }

    [RelayCommand]
    private void NewBlankTab()
    {
        var document = new CardDesignDocument { Name = $"Untitled {_untitledCounter++}" };
        OpenNewTab(document);
    }

    [RelayCommand]
    private void DuplicateActiveTab()
    {
        if (ActiveTab is null) return;

        var json = System.Text.Json.JsonSerializer.Serialize(ActiveTab.Document);
        var copy = System.Text.Json.JsonSerializer.Deserialize<CardDesignDocument>(json)!;
        copy.Id = System.Guid.NewGuid().ToString("N");
        copy.Name += " (copy)";
        OpenNewTab(copy);
    }

    [RelayCommand]
    private void CloseTab(CardDesignTabViewModel? tab)
    {
        tab ??= ActiveTab;
        if (tab is null) return;

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (ActiveTab == tab)
        {
            ActiveTab = Tabs.Count == 0 ? null : Tabs[System.Math.Max(0, index - 1)];
        }

        if (Tabs.Count == 0)
        {
            _navigationService.GoBack();
        }
    }

    [RelayCommand]
    private void SelectTab(CardDesignTabViewModel tab) => ActiveTab = tab;

    [RelayCommand]
    private void GoHome() => _navigationService.NavigateToRoot<WelcomeViewModel>();

    [RelayCommand]
    private void GoBack() => _navigationService.GoBack();
}