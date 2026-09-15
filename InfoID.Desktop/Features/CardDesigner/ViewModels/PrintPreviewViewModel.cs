using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Services;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>
/// Print Preview (Part 41): shows front/back at accurate physical proportions with
/// bleed/safe-zone guides and evaluated sample data, using the same ThumbnailRenderer
/// that draws Recent Cards thumbnails (with its optional data-binding/bleed extras
/// turned on here) rather than a third rendering implementation.
///
/// Deliberately does NOT include a "Print" action, printer selection, or a copy count:
/// there is no printing engine in this codebase (Part 42 is a separate, unbuilt module),
/// and a "Copies" field or "Print" button with nothing behind it would be exactly the
/// kind of non-functional control this project's own rules forbid. This dialog previews
/// appearance only, honestly.
/// </summary>
public sealed partial class PrintPreviewViewModel : DialogViewModelBase<bool>
{
    private readonly IDataBindingEvaluator _evaluator;
    private readonly IDesignAssetService _assetService;
    private static readonly IReadOnlyDictionary<string, string> EmptyRecord = new Dictionary<string, string>();

    public CardDesignDocument Document { get; }

    public ObservableCollection<PreviewRecord> Records { get; }

    [ObservableProperty]
    private PreviewRecord? _selectedRecord;

    [ObservableProperty]
    private DesignerSideView _activeSideView = DesignerSideView.Both;

    /// <summary>Front/Back panel visibility, computed from ActiveSideView -- "Both"
    /// shows both, "Front"/"Back" show just the one. Exposed as plain bools (rather than
    /// requiring a not-equal XAML converter) so the View can bind IsVisible directly,
    /// same idiom as SelectToolActive elsewhere in this codebase.</summary>
    public bool ShowFrontPanel => ActiveSideView != DesignerSideView.Back;
    public bool ShowBackPanel => ActiveSideView != DesignerSideView.Front;

    partial void OnActiveSideViewChanged(DesignerSideView value)
    {
        OnPropertyChanged(nameof(ShowFrontPanel));
        OnPropertyChanged(nameof(ShowBackPanel));
    }

    /// <summary>Defaults on, unlike the live canvas's own guides: the entire point of
    /// opening a print preview is to check bleed/safe-zone placement before printing.</summary>
    [ObservableProperty]
    private bool _showBleedAndSafeZone = true;

    public PrintPreviewViewModel(CardDesignDocument document, IDataBindingEvaluator evaluator, IPreviewDataProvider previewDataProvider, IDesignAssetService assetService)
    {
        Document = document;
        _evaluator = evaluator;
        _assetService = assetService;
        Records = new ObservableCollection<PreviewRecord>(previewDataProvider.GetSampleRecords());
        SelectedRecord = Records.FirstOrDefault();
    }

    /// <summary>Passed straight through to ThumbnailRenderer's RenderOptions so
    /// Image/Photo/Signature elements show the real picture instead of a placeholder box
    /// -- exactly what a print preview needs to be trustworthy before printing.</summary>
    public string? ResolveAssetPath(string? assetReference) => _assetService.ResolveToFullPath(assetReference);

    public string ResolveText(TextElement text) =>
        string.IsNullOrEmpty(text.DataBindingExpression)
            ? text.Text
            : _evaluator.Evaluate(text.DataBindingExpression, SelectedRecord?.Fields ?? EmptyRecord);

    public string ResolveField(DataFieldElement field)
    {
        var placeholder = $"{{{{{field.FieldKey}}}}}";
        return _evaluator.Evaluate(placeholder, SelectedRecord?.Fields ?? EmptyRecord);
    }

    public bool IsVisibleNow(DesignerElement element) =>
        _evaluator.EvaluateCondition(element.VisibilityCondition, SelectedRecord?.Fields ?? EmptyRecord);

    [RelayCommand]
    private void NextRecord()
    {
        if (Records.Count == 0) return;
        var index = SelectedRecord is null ? -1 : Records.IndexOf(SelectedRecord);
        SelectedRecord = Records[(index + 1) % Records.Count];
    }

    [RelayCommand]
    private void PreviousRecord()
    {
        if (Records.Count == 0) return;
        var index = SelectedRecord is null ? 0 : Records.IndexOf(SelectedRecord);
        SelectedRecord = Records[(index - 1 + Records.Count) % Records.Count];
    }

    [RelayCommand]
    private void SetSideFront() => ActiveSideView = DesignerSideView.Front;

    [RelayCommand]
    private void SetSideBack() => ActiveSideView = DesignerSideView.Back;

    [RelayCommand]
    private void SetSideBoth() => ActiveSideView = DesignerSideView.Both;

    [RelayCommand]
    private void ToggleBleedAndSafeZone() => ShowBleedAndSafeZone = !ShowBleedAndSafeZone;

    [RelayCommand]
    private void Close() => RequestClose(true);
}
