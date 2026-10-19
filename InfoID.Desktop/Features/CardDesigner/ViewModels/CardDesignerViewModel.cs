using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Core.Services;
using InfoID.Desktop.Features.BlankCard.Models;
using InfoID.Desktop.Features.CardDesigner.Models;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.Printing.Services;
using InfoID.Desktop.Features.Templates.Models;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

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
    private readonly IUserPreferencesService _preferences;
    private readonly IDesignChecker _designChecker;
    private readonly IThumbnailService _thumbnailService;
    private readonly IDialogService _dialogService;
    private readonly IInfoIdFileService _infoIdFileService;
    private readonly IRecentFilesService _recentFilesService;
    private readonly IImageEditingService _imageEditingService;
    private readonly ICameraService _cameraService;
    private readonly IFaceDetectionService _faceDetectionService;
    private readonly IPrinterService _printerService;
    private readonly IPrintStatusStore _printStatusStore;
    private readonly ICardFormatCatalogService _cardFormatCatalogService;
    private int _untitledCounter = 1;

    /// <summary>Right-panel visibility (Part 24/75). Session-only (not persisted) --
    /// these are workspace-layout preferences, not part of any saved design. Both
    /// default to visible, matching current/existing behaviour before these toggles
    /// existed.</summary>
    [ObservableProperty]
    private bool _showPropertiesPanel = true;

    [ObservableProperty]
    private bool _showLayersPanel = true;

    [RelayCommand]
    private void ToggleShowPropertiesPanel() => ShowPropertiesPanel = !ShowPropertiesPanel;

    [RelayCommand]
    private void ToggleShowLayersPanel() => ShowLayersPanel = !ShowLayersPanel;

    public CardDesignerViewModel(
        INavigationService navigationService, IDesignClipboard clipboard, ICardDesignRepository repository,
        IFilePickerService filePicker, IDesignAssetService assetService,
        IDataBindingEvaluator evaluator, IPreviewDataProvider previewData, IUserPreferencesService preferences,
        IDesignChecker designChecker, IThumbnailService thumbnailService, IDialogService dialogService,
        IInfoIdFileService infoIdFileService, IRecentFilesService recentFilesService,
        IImageEditingService imageEditingService, ICameraService cameraService,
        IFaceDetectionService faceDetectionService, IPrinterService printerService,
        IPrintStatusStore printStatusStore, ICardFormatCatalogService cardFormatCatalogService)
    {
        _navigationService = navigationService;
        _clipboard = clipboard;
        _repository = repository;
        _filePicker = filePicker;
        _assetService = assetService;
        _evaluator = evaluator;
        _previewData = previewData;
        _preferences = preferences;
        _designChecker = designChecker;
        _thumbnailService = thumbnailService;
        _dialogService = dialogService;
        _infoIdFileService = infoIdFileService;
        _recentFilesService = recentFilesService;
        _imageEditingService = imageEditingService;
        _cameraService = cameraService;
        _faceDetectionService = faceDetectionService;
        _printerService = printerService;
        _printStatusStore = printStatusStore;
        _cardFormatCatalogService = cardFormatCatalogService;
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
        // CardDesignerViewModel is a singleton (see App.axaml.cs) so its Tabs survive a
        // Home round trip -- but that also means OnNavigatedTo now fires on every mere
        // *revisit* of this page (e.g. jumping back from Home via the header's Windows
        // menu), not just on "open a new design". A null parameter means exactly that:
        // no new document was asked for, so if there's already something open, leave
        // Tabs/ActiveTab untouched instead of adding another blank "Untitled" tab every
        // time. Only a genuinely first-ever visit (no tabs yet at all) falls through to
        // the "new blank document" behavior below.
        if (parameter is null && Tabs.Count > 0) return;

        if (parameter is OpenDesignWithDataRequest dataRequest)
        {
            await OpenWithDataAsync(dataRequest);
            return;
        }

        if (parameter is long templateId)
        {
            // Priority 11 fix: reuse an already-open tab for this same database-backed
            // design instead of always opening a duplicate one. A design is uniquely
            // identified for this purpose by its PersistedTemplateId -- the same id
            // OnSaveVersions/BuildDocumentFromNavigationParameter already treat as the
            // database identity of a saved design.
            var existingTab = Tabs.FirstOrDefault(t => t.Document.PersistedTemplateId == templateId);
            if (existingTab is not null)
            {
                ActiveTab = existingTab;
                return;
            }

            var document = await _repository.LoadAsync(templateId);
            if (document is not null)
            {
                await _repository.MarkOpenedAsync(templateId);
                OpenNewTab(document);
                return;
            }
        }
        else if (parameter is OpenFileDesignRequest fileRequest)
        {
            await OpenFileAsNewOrExistingTabAsync(fileRequest.FilePath);
            return;
        }

        var newDocument = BuildDocumentFromNavigationParameter(parameter);

        // Blank Card (a plain format or a custom size) opens showing just the front --
        // most blank cards start single-sided and the user adds a back only if they
        // need one. A Template opens showing both sides at once, since templates are
        // commonly designed as a front+back pair from the start. Either way this is
        // only the *initial* view -- the Front/Back/Both toggle in the toolbar
        // (SetSideFront/SetSideBack/SetSideBoth) still works exactly the same
        // afterwards.
        var initialSideView = parameter is CardFormatOption or CustomCardSizeResult
            ? DesignerSideView.Front
            : DesignerSideView.Both;

        OpenNewTab(newDocument, initialSideView);
    }

    /// <summary>Switching designs finishes editing the previous one's data grid.</summary>
    partial void OnActiveTabChanged(CardDesignTabViewModel? oldValue, CardDesignTabViewModel? newValue)
    {
        oldValue?.SaveDatabaseRows(includeCurrentRow: true);
    }

    /// <summary>InfoID is closing: save every open design's pending data-grid rows
    /// (Internal Database). Nothing can be asked at this point; failures are logged.</summary>
    public void SavePendingDatabaseRows()
    {
        foreach (var tab in Tabs)
        {
            try { tab.SaveDatabaseRows(includeCurrentRow: true); }
            catch (System.Exception) { /* SaveDatabaseRows never throws; belt and braces while exiting */ }
        }
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

    /// <summary>Opens <paramref name="document"/> as a brand-new tab. <paramref
    /// name="initialSideView"/> is null for every caller that doesn't care (New Blank
    /// Tab, Duplicate Tab, Version History restore, file Import/Open-by-id) -- those
    /// keep CardDesignTabViewModel's own default (DesignerSideView.Both) exactly as
    /// before. Only OpenFromParameterAsync's Blank-Card/Template branch passes an
    /// explicit value.</summary>
    /// <summary>Opens (or reuses) a saved design, shows the given rows as its data and,
    /// when asked, opens the Print dialog -- e.g. Visitors page → Print badges.</summary>
    private async System.Threading.Tasks.Task OpenWithDataAsync(OpenDesignWithDataRequest request)
    {
        var tab = Tabs.FirstOrDefault(t => t.Document.PersistedTemplateId == request.TemplateId);
        if (tab is null)
        {
            var document = await _repository.LoadAsync(request.TemplateId);
            if (document is null)
            {
                await _dialogService.ShowDialogAsync<MessageDialogViewModel, bool>(new MessageDialogViewModel(
                    "Design not found",
                    "The badge design no longer exists. Choose another design on the Visitors page.",
                    isError: true));
                if (Tabs.Count == 0) _navigationService.GoBack();
                return;
            }
            await _repository.MarkOpenedAsync(request.TemplateId);
            OpenNewTab(document);
            tab = ActiveTab;
            if (tab is null) return;
        }
        else
        {
            ActiveTab = tab;
        }

        tab.AttachExternalData(request.Data, request.SourceType, request.SourceName, request.KeyColumn);

        if (request.OpenPrintDialog)
        {
            // After the designer page has been laid out, so the dialog opens over it.
            var target = tab;
            Avalonia.Threading.Dispatcher.UIThread.Post(
                () => target.ShowPrintDialogCommand.Execute(null),
                Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private void OpenNewTab(CardDesignDocument document, DesignerSideView? initialSideView = null)
    {
        var tab = new CardDesignTabViewModel(document, _clipboard, _repository, _filePicker, _assetService, _evaluator, _previewData, _preferences, _designChecker, _thumbnailService, _dialogService, _infoIdFileService, _recentFilesService, _imageEditingService, _cameraService, _faceDetectionService, _printerService, _printStatusStore, _cardFormatCatalogService);

        if (initialSideView is { } sideView)
        {
            tab.ActiveSideView = sideView;
            if (sideView == DesignerSideView.Front) tab.FocusedSide = CardSide.Front;
        }

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

    /// <summary>Version History (Part 60). Lives here rather than on
    /// CardDesignTabViewModel because restoring opens a brand-new tab -- only the
    /// designer (which owns Tabs) can do that; the tab itself has no reference back to
    /// its parent. A design with no PersistedTemplateId yet has never been saved, so
    /// there's nothing in the database to show a history for.</summary>
    [RelayCommand]
    private async Task ShowVersionHistory()
    {
        if (ActiveTab?.Document.PersistedTemplateId is not { } templateId) return;

        var versions = await _repository.GetVersionsAsync(templateId);
        var dialogViewModel = new VersionHistoryViewModel(versions);
        var selectedVersionId = await _dialogService.ShowDialogAsync<VersionHistoryViewModel, long?>(dialogViewModel);

        if (selectedVersionId is not { } versionId) return;

        var restored = await _repository.GetVersionSnapshotAsync(versionId);
        if (restored is null) return;

        // Opens as an editable copy, not tied to the original saved template -- saving
        // it creates a new design rather than silently overwriting whatever is
        // currently saved under the original one.
        restored.PersistedTemplateId = null;
        restored.Id = System.Guid.NewGuid().ToString("N");
        restored.Name += " (restored)";
        OpenNewTab(restored);
    }

    // ------------------------------------------------------------- .infoid files ----

    /// <summary>File > Export (Part 9/62). Deliberately exports the ACTIVE tab's current
    /// in-memory state, not the last-saved database version -- so exporting an unsaved
    /// design still exports what's actually on screen.</summary>
    [RelayCommand]
    private async Task ExportActiveTab()
    {
        if (ActiveTab is null) return;

        var suggestedName = SanitizeFileName(ActiveTab.Title);
        var path = await _filePicker.PickSaveFileAsync(
            "Export InfoID Design", "InfoID Design", new[] { "*.infoid" }, suggestedName, "infoid");
        if (path is null) return;

        try
        {
            await _infoIdFileService.ExportAsync(ActiveTab.Document, path);
            InfoID.Desktop.Core.Services.Audit.Record(InfoID.Domain.Common.Interfaces.AuditModules.Designs, "DesignExported",
                $"Design '{ActiveTab.Document.Name}' exported to {System.IO.Path.GetFileName(path)}",
                "Template", ActiveTab.Document.PersistedTemplateId, after: new { ActiveTab.Document.Name, File = path });
        }
        catch (InfoIdFileException ex)
        {
            await _dialogService.ShowDialogAsync<MessageDialogViewModel, bool>(
                new MessageDialogViewModel("Export Failed", ex.Message, isError: true));
        }
    }

    /// <summary>Priority 11 fix: shared by both entry points that can open a
    /// file-backed design (Recent Cards' OpenFromParameterAsync and File > Import
    /// below) so neither one can drift out of sync with the other on whether it checks
    /// for an already-open tab first -- the same "one shared implementation instead of
    /// two copies that can disagree" reasoning RecentCardListViewModelBase's own doc
    /// comment already calls out for Open/Delete/Pin. Matching is by SourceFilePath
    /// (case-insensitive, since Windows paths are), which OpenFileAsNewTabAsync already
    /// sets on every file-backed document it creates.</summary>
    private async Task OpenFileAsNewOrExistingTabAsync(string path)
    {
        var existingTab = Tabs.FirstOrDefault(t =>
            string.Equals(t.Document.SourceFilePath, path, System.StringComparison.OrdinalIgnoreCase));
        if (existingTab is not null)
        {
            ActiveTab = existingTab;
            return;
        }

        await OpenFileAsNewTabAsync(path);
    }

    /// <summary>File > Import (Part 9/62), and also used when opening a file-backed
    /// design from Recent Cards (OpenFromParameterAsync). Opens the design as a
    /// brand-new tab, marked as file-backed (SourceFilePath set) so Save writes back to
    /// this same file instead of creating a database Template row -- editing and saving
    /// an imported design updates that file, not "a new card" in the database. Tracked
    /// in IRecentFilesService immediately (not just on next Save) so it shows up in
    /// Recent Cards right away, matching how opening a database design calls
    /// MarkOpenedAsync.</summary>
    private async Task OpenFileAsNewTabAsync(string path)
    {
        CardDesignDocument document;
        try
        {
            document = await _infoIdFileService.ImportAsync(path);
        }
        catch (InfoIdFileException ex)
        {
            await _dialogService.ShowDialogAsync<MessageDialogViewModel, bool>(
                new MessageDialogViewModel("Open Failed", ex.Message, isError: true));
            return;
        }

        document.PersistedTemplateId = null;
        document.SourceFilePath = path;
        document.Id = System.Guid.NewGuid().ToString("N");
        OpenNewTab(document);

        await _recentFilesService.TrackAsync(path, document.Name);
    }

    [RelayCommand]
    private async Task ImportDesign()
    {
        var path = await _filePicker.PickOpenFileAsync("Import InfoID Design", "InfoID Design", new[] { "*.infoid" });
        if (path is null) return;

        await OpenFileAsNewOrExistingTabAsync(path);
        if (string.Equals(ActiveTab?.Document.SourceFilePath, path, System.StringComparison.OrdinalIgnoreCase))
            InfoID.Desktop.Core.Services.Audit.Record(InfoID.Domain.Common.Interfaces.AuditModules.Designs, "DesignImported",
                $"Design '{ActiveTab!.Document.Name}' imported from {System.IO.Path.GetFileName(path)}",
                after: new { ActiveTab.Document.Name, File = path });
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "Untitled" : cleaned;
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task CloseTab(CardDesignTabViewModel? tab)
    {
        tab ??= ActiveTab;
        if (tab is null) return;

        // Rows typed into the design's data grid (Internal Database) are saved before
        // the design closes; if some can't be, ask first.
        if (!await tab.ConfirmLeaveDatabaseAsync("Close")) return;
        if (!Tabs.Contains(tab)) return;

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        // Design changes held back by the audit log's 15-minute grouping are written
        // now that the design is closed (not left waiting for the timer).
        if (tab.Document.PersistedTemplateId is { } closedTemplateId)
            _ = InfoID.Desktop.Features.CardDesigner.Services.DesignEditAudit.FlushAsync(closedTemplateId);

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