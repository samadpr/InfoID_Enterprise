using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Services;
using InfoID.Desktop.Features.CardDesigner.History;
using InfoID.Desktop.Features.CardDesigner.Models;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.ViewModels.Base;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>
/// One open design tab (Part 6). Owns the document, per-tab zoom/pan/selection state
/// and its own undo/redo history -- switching tabs never mixes state between designs.
/// </summary>
public sealed partial class CardDesignTabViewModel : DocumentViewModelBase
{
    private readonly IDesignClipboard _clipboard;
    private readonly ICardDesignRepository _repository;
    private readonly IFilePickerService _filePicker;
    private readonly IDesignAssetService _assetService;
    private readonly IDataBindingEvaluator _evaluator;
    private readonly IPreviewDataProvider _previewDataProvider;
    private readonly IUserPreferencesService _preferences;
    private readonly IDesignChecker _designChecker;
    private readonly IThumbnailService _thumbnailService;
    private readonly IDialogService _dialogService;
    private readonly IInfoIdFileService _infoIdFileService;
    private readonly IRecentFilesService _recentFilesService;
    private readonly IImageEditingService _imageEditingService;
    private readonly ICameraService _cameraService;
    private readonly IFaceDetectionService _faceDetectionService;
    private CancellationTokenSource? _autosaveCts;

    public CardDesignDocument Document { get; }
    public DesignHistory History { get; } = new();

    /// <summary>Raised when something outside a live canvas drag changes the document
    /// (undo/redo, insert/delete/paste) so the canvas knows to redraw.</summary>
    public event EventHandler? DocumentChanged;

    public CardDesignTabViewModel(
        CardDesignDocument document, IDesignClipboard clipboard, ICardDesignRepository repository,
        IFilePickerService filePicker, IDesignAssetService assetService,
        IDataBindingEvaluator evaluator, IPreviewDataProvider previewDataProvider,
        IUserPreferencesService preferences, IDesignChecker designChecker, IThumbnailService thumbnailService,
        IDialogService dialogService, IInfoIdFileService infoIdFileService, IRecentFilesService recentFilesService,
        IImageEditingService imageEditingService, ICameraService cameraService,
        IFaceDetectionService faceDetectionService)
    {
        Document = document;
        _clipboard = clipboard;
        _repository = repository;
        _filePicker = filePicker;
        _assetService = assetService;
        _evaluator = evaluator;
        _previewDataProvider = previewDataProvider;
        _preferences = preferences;
        _designChecker = designChecker;
        _thumbnailService = thumbnailService;
        _dialogService = dialogService;
        _infoIdFileService = infoIdFileService;
        _recentFilesService = recentFilesService;
        _imageEditingService = imageEditingService;
        _cameraService = cameraService;
        _faceDetectionService = faceDetectionService;
        PreviewRecords = new ObservableCollection<PreviewRecord>(previewDataProvider.GetSampleRecords());
        SelectedPreviewRecord = PreviewRecords.FirstOrDefault();
        Title = document.Name;
        _autosaveEnabled = preferences.Current.AutosaveEnabled;

        HookSide(Document.Front);
        HookSide(Document.Back);
        RefreshBackgroundPreview();
    }

    // ------------------------------------------------------------------ rename ----

    /// <summary>True while the tab title is being edited inline (Part 20 -- "Rename
    /// Design"). Same double-click-to-edit shape as canvas text editing (Part 3), just
    /// scoped to the tab strip instead of the canvas.</summary>
    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    private string _renameText = string.Empty;

    [RelayCommand]
    private void BeginRename()
    {
        RenameText = Title;
        IsRenaming = true;
    }

    /// <summary>Commits RenameText as the new design name, or silently cancels if it's
    /// empty/unchanged/whitespace-only -- an accidental empty rename must never leave
    /// the design with a blank, unrecoverable title.</summary>
    [RelayCommand]
    private void CommitRename()
    {
        var trimmed = RenameText.Trim();
        if (trimmed.Length > 0 && trimmed != Title)
        {
            Title = trimmed;
            Document.Name = trimmed;
            NotifyDocumentChanged();
            MarkDirty();
        }

        IsRenaming = false;
    }

    [RelayCommand]
    private void CancelRename() => IsRenaming = false;

    // ------------------------------------------------------------------- view ----

    /// <summary>Grid visibility/snap are already real, persisted CardDesignDocument
    /// fields that CardCanvasView's DrawGrid already reads (Part 25) -- these commands
    /// are the missing piece: something in the UI that actually flips them. Document
    /// isn't an ObservableObject (it's the plain serialization DTO), so toggling it here
    /// goes through the same NotifyDocumentChanged/MarkDirty pipeline every other
    /// document mutation uses instead of relying on property-changed notification that
    /// doesn't exist for this type.</summary>
    /// <summary>Read-through properties so the toolbar can bind to grid/snap state with
    /// working change notification -- CardDesignDocument itself is a plain
    /// serialization DTO (not INotifyPropertyChanged), so binding straight to
    /// Document.ShowGrid would read once and then never visually update again after
    /// ToggleShowGridCommand flips it.</summary>
    public bool ShowGrid => Document.ShowGrid;
    public bool SnapToGrid => Document.SnapToGrid;

    [RelayCommand]
    private void ToggleShowGrid()
    {
        Document.ShowGrid = !Document.ShowGrid;
        OnPropertyChanged(nameof(ShowGrid));
        NotifyDocumentChanged();
        MarkDirty();
    }

    [RelayCommand]
    private void ToggleSnapToGrid()
    {
        Document.SnapToGrid = !Document.SnapToGrid;
        OnPropertyChanged(nameof(SnapToGrid));
    }

    /// <summary>Session-only (not persisted, matching the existing guides' own
    /// reasoning in the class doc comment above VerticalGuidesMm): whether the mm rulers
    /// along the top/left of the canvas are shown at all.</summary>
    [ObservableProperty]
    private bool _showRulers = true;

    [RelayCommand]
    private void ToggleShowRulers() => ShowRulers = !ShowRulers;



    /// <summary>Wires a side's element collection so every element already on it -- and
    /// every element added to it later (insert, paste, undo of a delete) -- has its
    /// PropertyChanged forwarded into the document-changed/dirty pipeline, and every
    /// element removed is unhooked so it can be garbage-collected. This is what makes a
    /// Properties-panel edit (or a canvas inline text edit) redraw the canvas and
    /// schedule autosave without any manual InvalidateVisual() call at the edit site
    /// (Part 78).</summary>
    private void HookSide(CardDesignSide side)
    {
        foreach (var element in side.Elements) HookElement(element);
        side.Elements.CollectionChanged += (_, e) =>
        {
            if (e.OldItems is not null)
            {
                foreach (DesignerElement removed in e.OldItems) UnhookElement(removed);
            }
            if (e.NewItems is not null)
            {
                foreach (DesignerElement added in e.NewItems) HookElement(added);
            }
        };
    }

    private void HookElement(DesignerElement element) => element.PropertyChanged += OnElementPropertyChanged;
    private void UnhookElement(DesignerElement element) => element.PropertyChanged -= OnElementPropertyChanged;

    private void OnElementPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Id/GroupId churn (e.g. during paste) already goes through explicit
        // NotifyDocumentChanged()/MarkDirty() calls at the call site; everything else --
        // Text, FontSize, ColorHex, X/Y/Width/Height/Rotation, alignment, etc. -- needs
        // this catch-all so editing from the Properties panel or an inline canvas editor
        // behaves identically to a drag/resize.
        NotifyDocumentChanged();
        MarkDirty();
    }
    [ObservableProperty]
    private string _saveStatusText = "Unsaved";

    /// <summary>Persisted via IUserPreferencesService (Part 7). Initialized from the
    /// service's already-loaded snapshot in the constructor, so a newly opened tab
    /// immediately reflects whatever the user last set -- no async load needed on the
    /// UI thread just to know this.</summary>
    [ObservableProperty]
    private bool _autosaveEnabled;

    [RelayCommand]
    private async Task ToggleAutosave()
    {
        AutosaveEnabled = !AutosaveEnabled;
        await _preferences.SetAutosaveEnabledAsync(AutosaveEnabled);

        // Turning autosave back on while there are already-unsaved changes should pick
        // up right where debounced autosave would have -- turning it off cancels any
        // pending autosave so a stale timer can't fire after the user explicitly opted
        // out (Part 7: "If autosave is disabled: do not automatically persist design
        // changes").
        if (AutosaveEnabled && IsDirty) ScheduleAutosave();
        else _autosaveCts?.Cancel();
    }

    [ObservableProperty]
    private DesignerSideView _activeSideView = DesignerSideView.Both;

    [ObservableProperty]
    private double _zoom = 1.0;

    [ObservableProperty]
    private double _panX;

    [ObservableProperty]
    private double _panY;

    [ObservableProperty]
    private CardSide _focusedSide = CardSide.Front;

    /// <summary>True while the "Pan" tool (H) is toggled on from the left tool rail --
    /// makes a plain left-drag pan the canvas instead of selecting/dragging elements,
    /// as an alternative to holding Space or the middle mouse button.</summary>
    [ObservableProperty]
    private bool _panToolActive;

    /// <summary>True while the "Line" tool is active -- a plain left-drag on the canvas
    /// draws a straight line from press to release instead of marquee-selecting (see
    /// CardCanvasView.OnPointerPressed/Moved/Released's DragMode.DrawLine branch and
    /// InsertDrawnLine below). Auto-clears itself after one line is drawn so the user
    /// isn't stuck in draw mode -- see InsertDrawnLine.</summary>
    [ObservableProperty]
    private bool _lineToolActive;

    /// <summary>Same idea as <see cref="LineToolActive"/> but for freehand drawing --
    /// a left-drag accumulates points into an open stroke instead of one straight
    /// segment (DragMode.DrawPen / InsertDrawnPen).</summary>
    [ObservableProperty]
    private bool _penToolActive;

    /// <summary>True only when none of the other tool modes are -- the top-toolbar
    /// "Select" button's IsChecked (Part 11 -- V/H moved out of the left rail into the
    /// toolbar as a proper tool-mode group, so exactly one of these always shows as
    /// active).</summary>
    public bool SelectToolActive => !PanToolActive && !LineToolActive && !PenToolActive;

    partial void OnPanToolActiveChanged(bool value) => OnPropertyChanged(nameof(SelectToolActive));
    partial void OnLineToolActiveChanged(bool value) => OnPropertyChanged(nameof(SelectToolActive));
    partial void OnPenToolActiveChanged(bool value) => OnPropertyChanged(nameof(SelectToolActive));

    /// <summary>The TextElement currently being edited inline on the canvas (Part 3),
    /// or null when no inline edit is active. The canvas skips drawing this element's
    /// static text while it's set (an overlay TextBox is shown in its place instead),
    /// and the Properties panel's own Text TextBox keeps working normally in parallel
    /// since both ultimately just set the same observable TextElement.Text property.</summary>
    [ObservableProperty]
    private TextElement? _inlineEditingElement;

    private string? _inlineEditOriginalText;

    /// <summary>Enters inline edit mode for a text element double-clicked on the canvas.</summary>
    public void BeginInlineEdit(TextElement element)
    {
        _inlineEditOriginalText = element.Text;
        InlineEditingElement = element;
    }

    /// <summary>Commits whatever is currently in the element's Text property (already
    /// live-bound to the overlay TextBox) and leaves inline edit mode.</summary>
    public void CommitInlineEdit()
    {
        if (InlineEditingElement is null) return;
        InlineEditingElement = null;
        _inlineEditOriginalText = null;
        NotifyDocumentChanged();
        MarkDirty();
    }

    /// <summary>Reverts to the text that was present when editing began and leaves
    /// inline edit mode -- Escape's contract per Part 3/80.</summary>
    public void CancelInlineEdit()
    {
        if (InlineEditingElement is null) return;
        if (_inlineEditOriginalText is not null) InlineEditingElement.Text = _inlineEditOriginalText;
        InlineEditingElement = null;
        _inlineEditOriginalText = null;
        NotifyDocumentChanged();
    }

    /// <summary>Session-only ruler guides (Part 21/23). Deliberately not part of
    /// CardDesignDocument / not serialized -- same reasoning as the existing document's
    /// GridSizeMm/ShowGrid design: adding persisted guides means a template-schema
    /// change, which is out of scope for this pass and would risk breaking older saved
    /// designs. Values are mm offsets from the card's top-left corner.</summary>
    public ObservableCollection<double> VerticalGuidesMm { get; } = new();
    public ObservableCollection<double> HorizontalGuidesMm { get; } = new();

    /// <summary>Live preview while dragging a new guide out of a ruler (Part 21) --
    /// null when no drag is in progress. The canvas reads this each render to draw a
    /// dashed preview line before the guide is actually committed on release.</summary>
    [ObservableProperty] private double? _guidePreviewMm;
    [ObservableProperty] private GuideOrientation _guidePreviewOrientation;

    public void AddVerticalGuide(double mm)
    {
        if (mm < 0 || mm > Document.WidthMm) return; // dropped outside the card -> discard, don't create a guide
        VerticalGuidesMm.Add(Math.Round(mm, 2));
    }

    public void AddHorizontalGuide(double mm)
    {
        if (mm < 0 || mm > Document.HeightMm) return;
        HorizontalGuidesMm.Add(Math.Round(mm, 2));
    }

    /// <summary>Removes the guide nearest to the given mm position, if it's within
    /// tolerance -- used for double-click/drag-back-to-ruler delete.</summary>
    public void RemoveVerticalGuideNear(double mm, double toleranceMm = 1.5)
    {
        var closest = VerticalGuidesMm.OrderBy(v => Math.Abs(v - mm)).FirstOrDefault(v => Math.Abs(v - mm) <= toleranceMm, double.NaN);
        if (!double.IsNaN(closest)) VerticalGuidesMm.Remove(closest);
    }

    public void RemoveHorizontalGuideNear(double mm, double toleranceMm = 1.5)
    {
        var closest = HorizontalGuidesMm.OrderBy(v => Math.Abs(v - mm)).FirstOrDefault(v => Math.Abs(v - mm) <= toleranceMm, double.NaN);
        if (!double.IsNaN(closest)) HorizontalGuidesMm.Remove(closest);
    }

    /// <summary>Session-only visibility/snap toggles for ruler guides (Part 27/28),
    /// mirroring the ShowGrid/SnapToGrid pattern -- default true matches the existing
    /// behaviour before these toggles existed (guides were always shown and always
    /// snapped to).</summary>
    [ObservableProperty]
    private bool _showGuides = true;

    [ObservableProperty]
    private bool _snapToGuides = true;

    /// <summary>Object/smart-guide snapping (other elements' edges/centers while
    /// dragging) already existed and worked before this toggle did -- default true
    /// preserves that exact prior behaviour; this just adds the missing on/off switch
    /// (Part 28).</summary>
    [ObservableProperty]
    private bool _snapToObjects = true;

    [RelayCommand]
    private void ToggleSnapToObjects() => SnapToObjects = !SnapToObjects;

    // -------------------------------------------------------------- design checker ----

    /// <summary>Findings from the last RunDesignCheck (Part 79). Empty until the user
    /// actually runs a check -- this deliberately does not auto-run on every edit, since
    /// the overlap check alone is O(n^2) and running it on every keystroke/drag frame
    /// would be wasteful for something that's only useful right before printing/saving
    /// a finished design.</summary>
    public ObservableCollection<DesignIssue> DesignIssues { get; } = new();

    [RelayCommand]
    private void RunDesignCheck()
    {
        DesignIssues.Clear();
        foreach (var issue in _designChecker.Check(Document))
        {
            DesignIssues.Add(issue);
        }
    }

    /// <summary>Clicking a finding selects the affected element, switching to its side
    /// first if it's on the one not currently focused (Part 79: "Allow clicking an
    /// issue to select the affected element").</summary>
    [RelayCommand]
    private void SelectIssue(DesignIssue issue)
    {
        if (issue.Element is null) return;

        if (FocusedSide != issue.Side) FocusedSide = issue.Side;
        SelectOnly(issue.Element);
    }

    // -------------------------------------------------------------- print preview ----

    [RelayCommand]
    private async Task ShowPrintPreview()
    {
        var previewViewModel = new PrintPreviewViewModel(Document, _evaluator, _previewDataProvider, _assetService);
        await _dialogService.ShowDialogAsync<PrintPreviewViewModel, bool>(previewViewModel);
    }

    // ------------------------------------------------------------------ crop -------

    /// <summary>Opens the interactive crop editor for an Image or Photo element (Part
    /// 38). Handles both element types here rather than splitting into two commands,
    /// since the only difference is which three properties get written back on Apply --
    /// the dialog itself, the math, and the aspect-ratio-from-element-size logic are
    /// identical either way.</summary>
    [RelayCommand]
    private async Task EditCrop(DesignerElement element)
    {
        var aspectRatio = element.Height > 0 ? element.Width / element.Height : 1.0;

        (double x, double y, double zoom) initial = element switch
        {
            ImageElement img => (img.CropX, img.CropY, img.CropZoom),
            PhotoElement photo => (photo.CropX, photo.CropY, photo.CropZoom),
            _ => (0, 0, 1.0),
        };

        var assetReference = element switch
        {
            ImageElement img => img.AssetReference,
            PhotoElement photo => photo.AssetReference,
            _ => null,
        };
        var fullPath = _assetService.ResolveToFullPath(assetReference);

        var dialogViewModel = new CropEditorViewModel(fullPath, aspectRatio, initial.x, initial.y, initial.zoom);
        var applied = await _dialogService.ShowDialogAsync<CropEditorViewModel, bool>(dialogViewModel);
        if (!applied) return;

        switch (element)
        {
            case ImageElement img:
                img.CropX = dialogViewModel.CropX;
                img.CropY = dialogViewModel.CropY;
                img.CropZoom = dialogViewModel.CropZoom;
                break;
            case PhotoElement photo:
                photo.CropX = dialogViewModel.CropX;
                photo.CropY = dialogViewModel.CropY;
                photo.CropZoom = dialogViewModel.CropZoom;
                break;
        }

        NotifyDocumentChanged();
        MarkDirty();
    }

    [RelayCommand]
    private void ToggleShowGuides() => ShowGuides = !ShowGuides;

    [RelayCommand]
    private void ToggleSnapToGuides() => SnapToGuides = !SnapToGuides;

    [RelayCommand]
    private void ClearGuides()
    {
        VerticalGuidesMm.Clear();
        HorizontalGuidesMm.Clear();
    }

    public CardDesignSide FocusedSideModel => Document.GetSide(FocusedSide);

    /// <summary>Priority 6/7 fix: FocusedSideModel is a plain computed property with no
    /// backing [ObservableProperty] field of its own, so nothing ever told bindings on
    /// it (e.g. the Layers panel's ItemsSource="{Binding ActiveTab.FocusedSideModel.
    /// Elements}") to refresh when FocusedSide itself changed -- whether from clicking
    /// the other side on the canvas (OnPointerPressed sets tab.FocusedSide directly) or
    /// from the toolbar's Front/Back/Both buttons (see SetSideFront/SetSideBack below).
    /// This is the same "expression-bodied property depending on an [ObservableProperty]
    /// needs its own explicit notification" pattern already used just above for
    /// SelectToolActive/PanToolActive -- CommunityToolkit.Mvvm generates this
    /// On[PropertyName]Changed partial method hook for every [ObservableProperty], we
    /// just weren't using it for FocusedSide until now. Without this, the Layers panel
    /// stayed permanently stuck showing whichever side was focused when the tab was
    /// first opened (Front, by default), no matter which side you later switched to --
    /// exactly the reported "Back-side layers are missing/not displayed correctly" bug,
    /// and the same underlying gap meant insert/edit commands (which all route through
    /// FocusedSideModel) could silently target the wrong side after switching views via
    /// the toolbar rather than by clicking on the canvas.</summary>
    partial void OnFocusedSideChanged(CardSide value)
    {
        OnPropertyChanged(nameof(FocusedSideModel));

        // Priority 6 fix (related): SelectedElements is a single shared collection, not
        // one per side (see its declaration below) -- without this, an element selected
        // on the side you're leaving would stay "selected" after switching, leaving the
        // Properties panel editing an element that isn't even part of the side now
        // being viewed, and DrawSelectionOverlay would try to draw that stale element's
        // outline using the new side's own card rect (a different physical side's
        // coordinate space), producing a meaningless, misplaced selection box. Clearing
        // on every focus change keeps "the active side receives selection/editing
        // operations" strictly true.
        ClearSelection();
        RefreshBackgroundPreview();
    }

    // ------------------------------------------------------------------ background ----

    /// <summary>True when the currently focused side has an uploaded background image
    /// (BackgroundKind.Image with a resolvable AssetReference) -- drives the Properties
    /// panel's Background section (Edit/Remove enabled, a real thumbnail shown instead
    /// of the "Empty" placeholder).</summary>
    public bool HasBackgroundImage =>
        FocusedSideModel.Background.Kind == BackgroundKind.Image &&
        !string.IsNullOrEmpty(FocusedSideModel.Background.AssetReference);

    /// <summary>The Properties panel's Background thumbnail. Refreshed explicitly
    /// (BackgroundSettings is a plain class, not an ObservableObject, so there's no
    /// PropertyChanged to react to automatically) whenever it could have changed:
    /// switching focused side, or the Browse/Edit/Remove commands below.</summary>
    [ObservableProperty]
    private Avalonia.Media.Imaging.Bitmap? _backgroundPreviewImage;

    private void RefreshBackgroundPreview()
    {
        var background = FocusedSideModel.Background;
        var fullPath = background.Kind == BackgroundKind.Image
            ? _assetService.ResolveToFullPath(background.AssetReference)
            : null;

        // Dispose the outgoing bitmap explicitly -- unlike CardCanvasView's
        // _bitmapCache (capped by the design's own finite set of unique asset files),
        // this single preview slot gets reloaded on every Browse/Edit/side-switch, so
        // leaving each old one for the GC to eventually collect would accumulate
        // native bitmap handles over a long editing session.
        BackgroundPreviewImage?.Dispose();
        BackgroundPreviewImage = fullPath is not null ? _imageEditingService.Load(fullPath) : null;
        OnPropertyChanged(nameof(HasBackgroundImage));
    }

    /// <summary>Properties panel's "Browse..." button (shown when nothing is selected --
    /// the Background section is a per-side document setting, not an element). Routes
    /// the picked file through the same ImageEditorDialogViewModel every other image
    /// entry point uses (RunImageEditorAsync), so a background image can be rotated/
    /// color-adjusted/background-removed/face-detected-and-cropped before it's actually
    /// set as the background, exactly like Browse Image already does for inserted
    /// elements.</summary>
    [RelayCommand]
    private async Task BrowseBackgroundImage()
    {
        var path = await _filePicker.PickImageFileAsync("Choose Background Image");
        if (path is null) return;

        var sourceBitmap = _imageEditingService.Load(path);
        var assetReference = await RunImageEditorAsync(sourceBitmap);
        if (assetReference is null) return;

        var background = FocusedSideModel.Background;
        background.Kind = BackgroundKind.Image;
        background.AssetReference = assetReference;

        RefreshBackgroundPreview();
        NotifyDocumentChanged();
        MarkDirty();
    }

    /// <summary>Re-opens the Image Editor for the ALREADY-SET background image -- same
    /// "resolve asset -> dialog -> write back" shape as EditImage (for placed Image/
    /// Photo elements) and EditCrop (for re-cropping in place), deliberately following
    /// that existing pattern rather than inventing a new one.</summary>
    [RelayCommand]
    private async Task EditBackgroundImage()
    {
        var background = FocusedSideModel.Background;
        var fullPath = _assetService.ResolveToFullPath(background.AssetReference);
        if (fullPath is null) return;

        Avalonia.Media.Imaging.Bitmap sourceBitmap;
        try
        {
            sourceBitmap = _imageEditingService.Load(fullPath);
        }
        catch (Exception)
        {
            return; // missing/corrupt asset file -- fail quietly, same as EditImage
        }

        var newAssetReference = await RunImageEditorAsync(sourceBitmap);
        if (newAssetReference is null) return;

        background.AssetReference = newAssetReference;
        RefreshBackgroundPreview();
        NotifyDocumentChanged();
        MarkDirty();
    }

    /// <summary>Clears the background image and reverts to a plain solid-color
    /// background (white, BackgroundSettings' own default) rather than leaving Kind on
    /// Image with a null AssetReference, which would just render as a broken-looking
    /// blank/placeholder instead of a clean, intentional "no image" state.</summary>
    [RelayCommand]
    private void RemoveBackgroundImage()
    {
        var background = FocusedSideModel.Background;
        background.Kind = BackgroundKind.Solid;
        background.AssetReference = null;

        RefreshBackgroundPreview();
        NotifyDocumentChanged();
        MarkDirty();
    }

    /// <summary>Exposed so CardCanvasView can resolve Image/Photo/SignatureElement
    /// asset references to a real file path for rendering, without the canvas control
    /// itself needing a DI-constructed reference to the same singleton service.</summary>
    public IDesignAssetService AssetService => _assetService;

    // ------------------------------------------------------------- data preview ----

    /// <summary>Sample records to preview dynamic fields/conditional visibility against
    /// (Part 43). See SamplePreviewDataProvider's own doc comment: these are placeholder
    /// data, not real cardholders, since the Cardholder Management module doesn't exist
    /// in this codebase yet.</summary>
    public ObservableCollection<PreviewRecord> PreviewRecords { get; }

    [ObservableProperty] private PreviewRecord? _selectedPreviewRecord;

    /// <summary>Off by default: with preview off, dynamic-field elements show their raw
    /// "{{FieldName}}" placeholder (Part 24 -- "visually distinguishable while
    /// designing") and every element is visible regardless of its VisibilityCondition,
    /// so conditional elements aren't invisible while you're trying to design them. With
    /// preview on, both are evaluated against SelectedPreviewRecord.</summary>
    [ObservableProperty] private bool _previewModeEnabled;

    partial void OnPreviewModeEnabledChanged(bool value) => NotifyDocumentChanged();
    partial void OnSelectedPreviewRecordChanged(PreviewRecord? value) => NotifyDocumentChanged();

    [RelayCommand]
    private void NextPreviewRecord()
    {
        if (PreviewRecords.Count == 0) return;
        var index = SelectedPreviewRecord is null ? -1 : PreviewRecords.IndexOf(SelectedPreviewRecord);
        SelectedPreviewRecord = PreviewRecords[(index + 1) % PreviewRecords.Count];
    }

    [RelayCommand]
    private void PreviousPreviewRecord()
    {
        if (PreviewRecords.Count == 0) return;
        var index = SelectedPreviewRecord is null ? 0 : PreviewRecords.IndexOf(SelectedPreviewRecord);
        SelectedPreviewRecord = PreviewRecords[(index - 1 + PreviewRecords.Count) % PreviewRecords.Count];
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyRecord = new Dictionary<string, string>();

    /// <summary>What CardCanvasView should actually draw for a data-bound piece of text
    /// (a TextElement's DataBindingExpression, or a DataFieldElement's FieldKey). Not in
    /// preview mode -> the raw placeholder text, so the design surface stays honest
    /// about what's static vs. bound while you're working. In preview mode -> the
    /// evaluated value against whichever sample record is selected.</summary>
    public string ResolveDisplayText(string rawPlaceholderText, string bindingExpression)
    {
        if (!PreviewModeEnabled) return rawPlaceholderText;
        var record = SelectedPreviewRecord?.Fields ?? EmptyRecord;
        return _evaluator.Evaluate(bindingExpression, record);
    }

    /// <summary>Whether an element should be drawn at all right now, given its
    /// VisibilityCondition. Always true outside preview mode (see PreviewModeEnabled's
    /// doc comment) so conditional elements never simply vanish while designing.</summary>
    public bool IsElementVisibleNow(DesignerElement element)
    {
        if (!PreviewModeEnabled) return true;
        var record = SelectedPreviewRecord?.Fields ?? EmptyRecord;
        return _evaluator.EvaluateCondition(element.VisibilityCondition, record);
    }

    public ObservableCollection<DesignerElement> SelectedElements { get; } = new();

    public DesignerElement? PrimarySelection => SelectedElements.Count == 1 ? SelectedElements[0] : null;

    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;
    public bool HasSelection => SelectedElements.Count > 0;
    public bool HasMultiSelection => SelectedElements.Count > 1;
    public bool CanPaste => _clipboard.HasContent;

    // --------------------------------------------------------------- selection ----

    /// <summary>Selects the element, expanded to every member sharing its GroupId (Part 28).</summary>
    public void SelectOnly(DesignerElement? element)
    {
        SelectedElements.Clear();
        if (element is not null)
        {
            foreach (var e in ResolveGroupMembers(element)) SelectedElements.Add(e);
        }
        NotifySelectionChanged();
    }

    public void ToggleSelection(DesignerElement element)
    {
        var members = ResolveGroupMembers(element).ToList();
        if (members.Any(SelectedElements.Contains))
        {
            foreach (var m in members) SelectedElements.Remove(m);
        }
        else
        {
            foreach (var m in members) SelectedElements.Add(m);
        }
        NotifySelectionChanged();
    }

    private System.Collections.Generic.IEnumerable<DesignerElement> ResolveGroupMembers(DesignerElement element)
    {
        if (string.IsNullOrEmpty(element.GroupId))
        {
            yield return element;
            yield break;
        }

        foreach (var e in FocusedSideModel.Elements.Where(e => e.GroupId == element.GroupId))
        {
            yield return e;
        }
    }

    public void ClearSelection()
    {
        SelectedElements.Clear();
        NotifySelectionChanged();
    }

    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(PrimarySelection));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasMultiSelection));
    }

    public void NotifyDocumentChanged() => DocumentChanged?.Invoke(this, EventArgs.Empty);

    // ------------------------------------------------------------------ history ----

    [RelayCommand]
    private void Undo()
    {
        History.Undo();
        RefreshHistoryFlags();
        NotifySelectionChanged();
        NotifyDocumentChanged();
        MarkDirty();
    }

    [RelayCommand]
    private void Redo()
    {
        History.Redo();
        RefreshHistoryFlags();
        NotifySelectionChanged();
        NotifyDocumentChanged();
        MarkDirty();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedElements.Count == 0) return;

        var command = new DeleteElementsCommand(FocusedSideModel, SelectedElements.ToList());
        History.Execute(command);
        ClearSelection();
        RefreshHistoryFlags();
        MarkDirty();
    }

    // ----------------------------------------------------------------- inserts ----

    [RelayCommand]
    private void AddTextElement() => InsertElement(new TextElement { Name = "Text", X = 10, Y = 10, Width = 40, Height = 10 });

    /// <summary>Backs the Shapes flyout's search box -- filters ShapeCatalog.All by
    /// display name, case-insensitive, live as the user types (see FilteredShapePresets
    /// below). Kept per-tab (rather than shared/static) so each open design's flyout
    /// search box is independent, matching how every other per-tab UI state here works.</summary>
    [ObservableProperty]
    private string _shapeSearchText = string.Empty;

    /// <summary>The Shapes flyout's ItemsSource, grouped by category -- recomputed on
    /// every keystroke via OnShapeSearchTextChanged below. Cheap: ShapeCatalog.All is a
    /// small (~16 entry) in-memory list, not worth caching.</summary>
    public IReadOnlyList<ShapeCategoryGroup> FilteredShapeGroups => ShapeCatalog.SearchGrouped(ShapeSearchText);

    /// <summary>Drives the flyout's "No shapes found" empty state.</summary>
    public bool HasNoShapeMatches => FilteredShapeGroups.Count == 0;

    partial void OnShapeSearchTextChanged(string value)
    {
        OnPropertyChanged(nameof(FilteredShapeGroups));
        OnPropertyChanged(nameof(HasNoShapeMatches));
    }

    /// <summary>Inserts a real ShapeElement built from the chosen catalog preset --
    /// replaces what used to be seven near-identical AddRectangleElement/AddEllipseElement/
    /// etc. one-liners now that the Shapes flyout offers ~16 presets driven by
    /// ShapeCatalog instead of a fixed 3x3 grid. Positioned at a fixed default (10,10)
    /// like every other insertion command in this class -- see InsertElement's own doc
    /// comment for why (no click-drag insertion tool exists for any element type here).</summary>
    [RelayCommand]
    private void InsertShapePreset(ShapePreset preset)
    {
        var shape = preset.Create();
        shape.X = 10;
        shape.Y = 10;
        InsertElement(shape);
    }

    /// <summary>Shared by InsertImage/InsertPhoto/InsertSignature: opens the native file
    /// picker, copies the chosen file into InfoID's local assets folder (never storing
    /// the original absolute path -- Part 53), and reads its pixel size so the new
    /// element can default to a physically sensible size on the card instead of an
    /// arbitrary fixed box that ignores the picture's aspect ratio.</summary>
    private async Task<(string AssetReference, double WidthMm, double HeightMm)?> PickAndImportAsync(string dialogTitle, double defaultWidthMm)
    {
        var path = await _filePicker.PickImageFileAsync(dialogTitle);
        if (path is null) return null;

        string assetReference;
        try
        {
            assetReference = await _assetService.ImportAsync(path);
        }
        catch (Exception)
        {
            // A corrupted/locked/inaccessible source file must not crash the designer
            // (Part 65) -- the insert simply doesn't happen; SaveStatusText already
            // surfaces other failures the same low-key way elsewhere in this class.
            return null;
        }

        var widthMm = defaultWidthMm;
        var heightMm = defaultWidthMm;
        try
        {
            var fullPath = _assetService.ResolveToFullPath(assetReference);
            if (fullPath is not null)
            {
                using var bitmap = new Avalonia.Media.Imaging.Bitmap(fullPath);
                if (bitmap.PixelSize.Width > 0)
                {
                    heightMm = defaultWidthMm * bitmap.PixelSize.Height / bitmap.PixelSize.Width;
                }
            }
        }
        catch (Exception)
        {
            // Fall back to a square default if the picked file isn't actually a
            // decodable image -- still don't crash the designer.
        }

        return (assetReference, widthMm, heightMm);
    }

    [RelayCommand]
    private async Task InsertImage()
    {
        var picked = await PickAndImportAsync("Insert Image", defaultWidthMm: 30);
        if (picked is not { } result) return;
        InsertElement(new ImageElement
        {
            Name = "Image", AssetReference = result.AssetReference,
            X = 10, Y = 10, Width = result.WidthMm, Height = result.HeightMm,
        });
    }

    [RelayCommand]
    private async Task InsertPhoto()
    {
        var picked = await PickAndImportAsync("Insert Photo", defaultWidthMm: 20);
        if (picked is not { } result) return;
        InsertElement(new PhotoElement
        {
            Name = "Photo", AssetReference = result.AssetReference,
            X = 10, Y = 10, Width = result.WidthMm, Height = result.HeightMm,
        });
    }

    /// <summary>Priority 12: "Add Image" workflow. Replaces the old direct
    /// pick-file-and-insert-immediately behaviour for the generic Image tool (Photo and
    /// Signature keep their existing, distinct behaviour above/below -- the brief asks
    /// specifically for the "Image" insertion action to gain this workflow, not those
    /// two). "Browse Image" opens the native file picker, then routes the picked file
    /// through ImageEditorDialogViewModel before anything is inserted -- "Only the
    /// final edited image should be inserted into the canvas" per the brief.</summary>
    [RelayCommand]
    private async Task BrowseImageForInsert()
    {
        var path = await _filePicker.PickImageFileAsync("Insert Image");
        if (path is null) return;

        var bitmap = _imageEditingService.Load(path);
        await EditThenInsertImageAsync(bitmap);
    }

    /// <summary>"Take Photo / Use Camera" entry point. Opens CameraCaptureDialogViewModel,
    /// which owns device selection, live preview, countdown and retake (FR-PHS-1) and
    /// itself handles the "no camera available" case gracefully (see that class's own
    /// doc comment) -- including offering to fall through to Browse Image, which this
    /// method honours via CameraCaptureResult.FallThroughToBrowse. A successfully
    /// captured frame is routed through the exact same ImageEditorDialogViewModel
    /// Browse Image uses -- "Do not create two separate image editors" per the brief.</summary>
    [RelayCommand]
    private async Task TakePhotoForInsert()
    {
        var cameraViewModel = new CameraCaptureDialogViewModel(_cameraService);
        var result = await _dialogService.ShowDialogAsync<CameraCaptureDialogViewModel, CameraCaptureResult>(cameraViewModel);
        if (result is null) return; // dialog cancelled outright

        if (result.FallThroughToBrowse)
        {
            await BrowseImageForInsert();
            return;
        }

        if (result.CapturedBitmap is { } capturedBitmap)
        {
            await EditThenInsertImageAsync(capturedBitmap);
        }
    }

    private async Task EditThenInsertImageAsync(Avalonia.Media.Imaging.Bitmap sourceBitmap)
    {
        var assetReference = await RunImageEditorAsync(sourceBitmap);
        if (assetReference is null) return; // user cancelled the editor, or the edited file couldn't be imported

        const double defaultWidthMm = 30;
        var widthMm = defaultWidthMm;
        var heightMm = defaultWidthMm;
        try
        {
            var fullPath = _assetService.ResolveToFullPath(assetReference);
            if (fullPath is not null)
            {
                using var bitmap = new Avalonia.Media.Imaging.Bitmap(fullPath);
                if (bitmap.PixelSize.Width > 0)
                {
                    heightMm = defaultWidthMm * bitmap.PixelSize.Height / bitmap.PixelSize.Width;
                }
            }
        }
        catch (Exception)
        {
            // Fall back to a square default, same as PickAndImportAsync.
        }

        InsertElement(new ImageElement
        {
            Name = "Image", AssetReference = assetReference,
            X = 10, Y = 10, Width = widthMm, Height = heightMm,
        });
    }

    /// <summary>Shared by both EditThenInsertImageAsync (Browse/Take Photo -- inserts a
    /// brand-new element) and EditImage below (re-editing an already-placed image --
    /// updates the existing element in place): opens ImageEditorDialogViewModel,
    /// imports the result into the real assets store, and cleans up the temp file
    /// either way. Returns the new asset reference, or null if the user cancelled the
    /// editor or the edited file couldn't be imported.</summary>
    private async Task<string?> RunImageEditorAsync(Avalonia.Media.Imaging.Bitmap sourceBitmap)
    {
        var editorViewModel = new ImageEditorDialogViewModel(sourceBitmap, _imageEditingService, _faceDetectionService);
        var editResult = await _dialogService.ShowDialogAsync<ImageEditorDialogViewModel, ImageEditorResult>(editorViewModel);
        if (editResult is null) return null;

        try
        {
            return await _assetService.ImportAsync(editResult.EditedFilePath);
        }
        catch (Exception)
        {
            // Same "never crash the designer over a bad file" reasoning as
            // PickAndImportAsync above.
            return null;
        }
        finally
        {
            // The edited file is a temp file ImageEditingService created solely for
            // this import -- IDesignAssetService copies it into the real assets store,
            // so the temp copy is no longer needed either way.
            try { System.IO.File.Delete(editResult.EditedFilePath); } catch (Exception) { /* best-effort cleanup */ }
        }
    }

    /// <summary>Re-opens the Image Editor for an ALREADY-PLACED Image or Photo element
    /// (bound to a "Edit Image..." button in that element's Properties panel section,
    /// the same "resolve asset -> dialog -> write back" shape EditCrop above already
    /// uses for its own "Crop / Pan / Zoom..." button -- deliberately following that
    /// existing pattern rather than inventing a new one). Unlike
    /// EditThenInsertImageAsync, this updates the SAME element's AssetReference in
    /// place instead of inserting a new element -- the element keeps its existing
    /// position/size/rotation/data-binding/everything else, only its picture changes.</summary>
    [RelayCommand]
    private async Task EditImage(DesignerElement element)
    {
        var currentAssetReference = element switch
        {
            ImageElement img => img.AssetReference,
            PhotoElement photo => photo.AssetReference,
            _ => null,
        };

        var fullPath = _assetService.ResolveToFullPath(currentAssetReference);
        if (fullPath is null) return;

        Avalonia.Media.Imaging.Bitmap sourceBitmap;
        try
        {
            sourceBitmap = _imageEditingService.Load(fullPath);
        }
        catch (Exception)
        {
            // Missing/corrupt asset file -- fail quietly rather than crash the
            // designer, same reasoning as everywhere else this file touches a file
            // on disk that might not be there anymore.
            return;
        }

        var newAssetReference = await RunImageEditorAsync(sourceBitmap);
        if (newAssetReference is null) return;

        switch (element)
        {
            case ImageElement img: img.AssetReference = newAssetReference; break;
            case PhotoElement photo: photo.AssetReference = newAssetReference; break;
        }

        NotifyDocumentChanged();
        MarkDirty();
    }

    /// <summary>Properties panel's Parameters tab, "Browse Image" source button --
    /// replaces this Image element's picture with a freshly-picked file (distinct from
    /// EditImage above, which re-opens the Image Editor on the CURRENT picture; this
    /// one picks a different file first). Still routes through the same Image Editor
    /// before committing, same as every other image entry point in this class.</summary>
    [RelayCommand]
    private async Task ReplaceImageFromFile(DesignerElement element)
    {
        if (element is not ImageElement img) return;

        var path = await _filePicker.PickImageFileAsync("Choose Image");
        if (path is null) return;

        var sourceBitmap = _imageEditingService.Load(path);
        var newAssetReference = await RunImageEditorAsync(sourceBitmap);
        if (newAssetReference is null) return;

        img.AssetReference = newAssetReference;
        NotifyDocumentChanged();
        MarkDirty();
    }

    /// <summary>Properties panel's Parameters tab, "Get from camera" source button --
    /// same idea as ReplaceImageFromFile above, sourced from CameraCaptureDialogViewModel
    /// instead (the same dialog TakePhotoForInsert already uses), including its own
    /// "no camera available -> fall through to Browse Image" handling.</summary>
    [RelayCommand]
    private async Task ReplaceImageFromCamera(DesignerElement element)
    {
        if (element is not ImageElement img) return;

        var cameraViewModel = new CameraCaptureDialogViewModel(_cameraService);
        var result = await _dialogService.ShowDialogAsync<CameraCaptureDialogViewModel, CameraCaptureResult>(cameraViewModel);
        if (result is null) return;

        if (result.FallThroughToBrowse)
        {
            await ReplaceImageFromFile(img);
            return;
        }

        if (result.CapturedBitmap is not { } capturedBitmap) return;

        var newAssetReference = await RunImageEditorAsync(capturedBitmap);
        if (newAssetReference is null) return;

        img.AssetReference = newAssetReference;
        NotifyDocumentChanged();
        MarkDirty();
    }

    [RelayCommand]
    private async Task InsertSignature()
    {
        var picked = await PickAndImportAsync("Insert Signature", defaultWidthMm: 25);
        if (picked is not { } result) return;
        InsertElement(new SignatureElement
        {
            Name = "Signature", AssetReference = result.AssetReference,
            X = 10, Y = 10, Width = result.WidthMm, Height = result.HeightMm,
        });
    }

    [RelayCommand]
    private void AddBarcodeElement() => InsertElement(new BarcodeElement { Name = "Barcode", X = 10, Y = 10, Width = 40, Height = 15 });

    /// <summary>Part 93: QR Code now has a real renderer (BarcodeRenderer.cs, ZXing.Net)
    /// -- this command, and the rail button that calls it, didn't exist before that
    /// (the rail only ever had a permanently-disabled placeholder button with an
    /// honest "not implemented yet" tooltip; see CardDesignerView.axaml). Inserted
    /// square by default (Width==Height), matching how a QR Code actually renders --
    /// unlike a linear barcode, its natural aspect ratio is always 1:1.</summary>
    [RelayCommand]
    private void AddQrCodeElement() => InsertElement(new QrCodeElement { Name = "QR Code", X = 10, Y = 10, Width = 20, Height = 20 });

    /// <summary>Inserts a pure mail-merge placeholder (Part 35/45 -- "Insert Data Field").
    /// Distinct from AddTextElement: this element always shows "{{FieldKey}}" while
    /// designing and only resolves to a real value in Data Preview, so it's visually and
    /// semantically flagged (see the DataFieldElement DataTemplate) as a bound field
    /// rather than editable static text.</summary>

    [RelayCommand]
    private void AddDateTimeElement(string? displayFormat)
    {
        var format = Enum.TryParse<DateTimeDisplayFormat>(displayFormat, out var parsed)
            ? parsed
            : DateTimeDisplayFormat.DateTime;

        InsertElement(new DateTimeElement
        {
            Name = "Date / Time",
            X = 10,
            Y = 10,
            Width = 65,
            Height = 10,
            DisplayFormat = format,
            DateFormat = "dd-MM-yyyy",
            TimeFormat = "HH:mm:ss",
            FontFamily = "Segoe UI",
            FontSize = 24,
            Bold = false,
            Italic = false,
            Underline = false,
            ColorHex = "#000000",
            HorizontalAlignment = TextAlignmentX.Left,
            VerticalAlignment = TextAlignmentY.Top
        });
    }
    [RelayCommand]
    private void AddDataFieldElement() => InsertElement(new DataFieldElement { Name = "Data Field", X = 10, Y = 10, Width = 40, Height = 8 });

    /// <summary>Shared by every "Add ___" insertion command in this class (text, shapes,
    /// image, barcode, ...): appends element to FocusedSideModel on top of the z-order,
    /// wraps it in an undoable AddElementCommand, and selects it. Every caller passes a
    /// fully-built element at a fixed default position/size -- there is no click-drag
    /// "size it on the canvas" insertion tool for any element type in this codebase;
    /// the element lands pre-placed and pre-selected, ready to move/resize/rotate with
    /// the same generic transform handles every element already has.</summary>
    private void InsertElement(DesignerElement element)
    {
        var side = FocusedSideModel;
        element.ZIndex = side.Elements.Count == 0 ? 0 : side.Elements.Max(e => e.ZIndex) + 1;

        var command = new AddElementCommand(side, element);
        History.Execute(command);
        SelectOnly(element);
        RefreshHistoryFlags();
        MarkDirty();
    }

    /// <summary>Commits a line actually drawn with the Line tool (CardCanvasView's
    /// click-drag gesture) as a real ShapeElement -- reuses ShapeKind.Line's existing
    /// geometry/rendering/persistence untouched (ShapeRenderer), this just computes the
    /// bounding box from the two drawn points and picks LineFlipped so the line renders
    /// along whichever diagonal the user actually dragged, not always top-left-to-
    /// bottom-right. Reverts to the Select tool afterward -- drawing one line and then
    /// immediately being stuck in "click anywhere draws another line" mode would be a
    /// worse default than the click-to-insert-then-adjust pattern every other element
    /// already uses.</summary>
    public void InsertDrawnLine(double startXMm, double startYMm, double endXMm, double endYMm)
    {
        LineToolActive = false;

        var minX = Math.Min(startXMm, endXMm);
        var minY = Math.Min(startYMm, endYMm);
        var width = Math.Abs(endXMm - startXMm);
        var height = Math.Abs(endYMm - startYMm);
        if (width < 1 && height < 1) return; // accidental click, not a real drag

        var leftIsStart = startXMm <= endXMm;
        var leftY = leftIsStart ? startYMm : endYMm;
        var rightY = leftIsStart ? endYMm : startYMm;

        InsertElement(new ShapeElement
        {
            Name = "Line", Kind = ShapeKind.Line, FillEnabled = false,
            StrokeColorHex = "#1F2328", StrokeWidth = 1,
            X = minX, Y = minY, Width = Math.Max(width, 0.5), Height = Math.Max(height, 0.5),
            LineFlipped = leftY > rightY,
        });
    }

    /// <summary>Commits a freehand stroke drawn with the Pen tool as a real PenElement --
    /// pointsMm are absolute millimeters (already converted from device pixels by
    /// CardCanvasView), normalized here into PointsFraction (0..1 of the stroke's own
    /// bounding box, matching how PenRenderer draws them and how the element's normal
    /// resize handles will rescale the whole stroke later). Discards a sub-1mm capture
    /// as an accidental click, the same threshold InsertDrawnLine uses, and reverts to
    /// the Select tool afterward for the same reason InsertDrawnLine does.</summary>
    public void InsertDrawnPen(IReadOnlyList<(double X, double Y)> pointsMm)
    {
        PenToolActive = false;
        if (pointsMm.Count < 2) return;

        var minX = pointsMm.Min(p => p.X);
        var minY = pointsMm.Min(p => p.Y);
        var maxX = pointsMm.Max(p => p.X);
        var maxY = pointsMm.Max(p => p.Y);
        var width = maxX - minX;
        var height = maxY - minY;
        if (width < 1 && height < 1) return; // accidental click, not a real stroke

        var boxWidth = Math.Max(width, 0.5);
        var boxHeight = Math.Max(height, 0.5);

        var element = new PenElement
        {
            Name = "Drawing", X = minX, Y = minY, Width = boxWidth, Height = boxHeight,
            StrokeColorHex = "#1F2328", StrokeWidth = 1.2,
        };
        foreach (var p in pointsMm)
        {
            element.PointsFraction.Add(new PenPoint((p.X - minX) / boxWidth, (p.Y - minY) / boxHeight));
        }

        InsertElement(element);
    }

    [RelayCommand]
    private void SelectOnlyLayer(DesignerElement element) => SelectOnly(element);

    [RelayCommand]
    private void ToggleLayerVisible(DesignerElement element)
    {
        element.Visible = !element.Visible;
        NotifyDocumentChanged();
        MarkDirty();
    }

    [RelayCommand]
    private void ToggleLayerLocked(DesignerElement element)
    {
        element.Locked = !element.Locked;
        if (element.Locked) SelectedElements.Remove(element);
        NotifySelectionChanged();
        NotifyDocumentChanged();
        MarkDirty();
    }

    // ---------------------------------------------------------- clipboard/dupe ----

    [RelayCommand]
    private void Copy()
    {
        if (SelectedElements.Count == 0) return;
        _clipboard.SetContent(SelectedElements.ToList());
        OnPropertyChanged(nameof(CanPaste));
    }

    [RelayCommand]
    private void Cut()
    {
        if (SelectedElements.Count == 0) return;
        _clipboard.SetContent(SelectedElements.ToList());
        OnPropertyChanged(nameof(CanPaste));
        DeleteSelected();
    }

    [RelayCommand]
    private void Paste()
    {
        if (!_clipboard.HasContent) return;

        var side = FocusedSideModel;
        var clones = _clipboard.GetClones();
        var commands = new System.Collections.Generic.List<IDesignCommand>();

        foreach (var clone in clones)
        {
            clone.X += 5; // small offset so pasted copies aren't hidden exactly under the originals
            clone.Y += 5;
            clone.ZIndex = side.Elements.Count == 0 ? 0 : side.Elements.Max(e => e.ZIndex) + 1;
            commands.Add(new AddElementCommand(side, clone));
        }

        var composite = commands.Count == 1 ? commands[0] : new CompositeCommand("Paste", commands);
        History.Execute(composite);

        SelectedElements.Clear();
        foreach (var c in clones) SelectedElements.Add(c);
        NotifySelectionChanged();
        RefreshHistoryFlags();
        MarkDirty();
    }

    [RelayCommand]
    private void DuplicateSelected()
    {
        if (SelectedElements.Count == 0) return;
        Copy();
        Paste();
    }

    // -------------------------------------------------------------- grouping ----

    [RelayCommand]
    private void Group()
    {
        if (SelectedElements.Count < 2) return;

        var groupId = Guid.NewGuid().ToString("N");
        foreach (var e in SelectedElements) e.GroupId = groupId;
        NotifyDocumentChanged();
        MarkDirty();
    }

    [RelayCommand]
    private void Ungroup()
    {
        foreach (var e in SelectedElements.ToList()) e.GroupId = null;
        NotifyDocumentChanged();
        MarkDirty();
    }

    // ------------------------------------------------------------- alignment ----

    /// <summary>Axis-aligned bounding box (in document mm) of an element's actual
    /// rotated footprint. Object-alignment commands used to align on the raw,
    /// un-rotated X/Y/Width/Height, so a rotated element would visually land off from
    /// the others it was "aligned" with -- the bug behind Part 79 ("alignment doesn't
    /// work reliably"), most obvious with even a small accidental rotation like the one
    /// left over from a rotate-handle drag. Aligning on this rect instead always matches
    /// what the user actually sees on the canvas.</summary>
    private static Avalonia.Rect GetVisualBounds(DesignerElement el)
    {
        if (el.Rotation == 0) return new Avalonia.Rect(el.X, el.Y, el.Width, el.Height);

        var cx = el.X + el.Width / 2;
        var cy = el.Y + el.Height / 2;
        var rad = el.Rotation * Math.PI / 180.0;
        var cos = Math.Abs(Math.Cos(rad));
        var sin = Math.Abs(Math.Sin(rad));
        var rotatedW = el.Width * cos + el.Height * sin;
        var rotatedH = el.Width * sin + el.Height * cos;
        return new Avalonia.Rect(cx - rotatedW / 2, cy - rotatedH / 2, rotatedW, rotatedH);
    }

    [RelayCommand]
    private void AlignLeft() => Align(el => GetVisualBounds(el).Left, (el, v) => el.X += v - GetVisualBounds(el).Left, useMin: true);

    [RelayCommand]
    private void AlignRight() => Align(el => GetVisualBounds(el).Right, (el, v) => el.X += v - GetVisualBounds(el).Right, useMin: false);

    [RelayCommand]
    private void AlignCenterHorizontal()
    {
        if (SelectedElements.Count < 2) return;
        var target = (SelectedElements.Min(e => GetVisualBounds(e).Left) + SelectedElements.Max(e => GetVisualBounds(e).Right)) / 2;
        foreach (var e in SelectedElements)
        {
            var bounds = GetVisualBounds(e);
            e.X += target - (bounds.Left + bounds.Width / 2);
        }
        NotifyDocumentChanged();
        MarkDirty();
    }

    [RelayCommand]
    private void AlignTop() => AlignVertical(el => GetVisualBounds(el).Top, (el, v) => el.Y += v - GetVisualBounds(el).Top, useMin: true);

    [RelayCommand]
    private void AlignBottom() => AlignVertical(el => GetVisualBounds(el).Bottom, (el, v) => el.Y += v - GetVisualBounds(el).Bottom, useMin: false);

    [RelayCommand]
    private void AlignMiddleVertical()
    {
        if (SelectedElements.Count < 2) return;
        var target = (SelectedElements.Min(e => GetVisualBounds(e).Top) + SelectedElements.Max(e => GetVisualBounds(e).Bottom)) / 2;
        foreach (var e in SelectedElements)
        {
            var bounds = GetVisualBounds(e);
            e.Y += target - (bounds.Top + bounds.Height / 2);
        }
        NotifyDocumentChanged();
        MarkDirty();
    }

    private void Align(Func<DesignerElement, double> selector, Action<DesignerElement, double> apply, bool useMin)
    {
        if (SelectedElements.Count < 2) return;
        var target = useMin ? SelectedElements.Min(selector) : SelectedElements.Max(selector);
        foreach (var e in SelectedElements) apply(e, target);
        NotifyDocumentChanged();
        MarkDirty();
    }

    // --------------------------------------------------------------- distribute ----

    /// <summary>Distributes so the GAPS between consecutive elements' edges are equal
    /// (Part 18/34's "Distribute horizontally/vertically") -- the leftmost/topmost and
    /// rightmost/bottommost elements anchor the span and never move; only the ones
    /// between them are repositioned. This is the common "equal spacing" definition
    /// (Figma/Illustrator/PowerPoint default), not equal-center-spacing, which reads
    /// oddly once elements have different sizes.</summary>
    [RelayCommand]
    private void DistributeHorizontal() => Distribute(
        left: el => GetVisualBounds(el).Left,
        width: el => GetVisualBounds(el).Width,
        applyDelta: (el, delta) => el.X += delta);

    [RelayCommand]
    private void DistributeVertical() => Distribute(
        left: el => GetVisualBounds(el).Top,
        width: el => GetVisualBounds(el).Height,
        applyDelta: (el, delta) => el.Y += delta);

    private void Distribute(Func<DesignerElement, double> left, Func<DesignerElement, double> width, Action<DesignerElement, double> applyDelta)
    {
        if (SelectedElements.Count < 3) return; // nothing meaningful to space out with 0-2 elements

        var sorted = SelectedElements.OrderBy(left).ToList();
        var spanStart = left(sorted[0]);
        var spanEnd = left(sorted[sorted.Count - 1]) + width(sorted[sorted.Count - 1]);
        var totalWidth = sorted.Sum(width);
        var gap = (spanEnd - spanStart - totalWidth) / (sorted.Count - 1);

        var cursor = spanStart + width(sorted[0]) + gap;
        for (var i = 1; i < sorted.Count - 1; i++)
        {
            var el = sorted[i];
            var delta = cursor - left(el);
            applyDelta(el, delta);
            cursor += width(el) + gap;
        }

        NotifyDocumentChanged();
        MarkDirty();
    }

    private void AlignVertical(Func<DesignerElement, double> selector, Action<DesignerElement, double> apply, bool useMin)
    {
        if (SelectedElements.Count < 2) return;
        var target = useMin ? SelectedElements.Min(selector) : SelectedElements.Max(selector);
        foreach (var e in SelectedElements) apply(e, target);
        NotifyDocumentChanged();
        MarkDirty();
    }

    // --------------------------------------------------------------- z-order ----

    [RelayCommand]
    private void BringToFront() => Reorder(el => (FocusedSideModel.Elements.Max(x => x.ZIndex)) + 1);

    [RelayCommand]
    private void SendToBack() => Reorder(el => (FocusedSideModel.Elements.Min(x => x.ZIndex)) - 1);

    [RelayCommand]
    private void BringForward() => Reorder(el => el.ZIndex + 1);

    [RelayCommand]
    private void SendBackward() => Reorder(el => el.ZIndex - 1);

    private void Reorder(Func<DesignerElement, int> newZIndex)
    {
        if (SelectedElements.Count == 0) return;

        var commands = SelectedElements
            .Select(e => (IDesignCommand)new ReorderElementCommand(e, e.ZIndex, newZIndex(e)))
            .ToList();

        var composite = commands.Count == 1 ? commands[0] : new CompositeCommand("Reorder", commands);
        History.Execute(composite);
        NotifyDocumentChanged();
        RefreshHistoryFlags();
        MarkDirty();
    }

    // ---------------------------------------------------------------- side/zoom ----

    [RelayCommand]
    private void SelectTool()
    {
        PanToolActive = false;
        LineToolActive = false;
        PenToolActive = false;
    }

    [RelayCommand]
    private void PanTool()
    {
        PanToolActive = true;
        LineToolActive = false;
        PenToolActive = false;
    }

    [RelayCommand]
    private void LineTool()
    {
        PanToolActive = false;
        PenToolActive = false;
        LineToolActive = true;
    }

    [RelayCommand]
    private void PenTool()
    {
        PanToolActive = false;
        LineToolActive = false;
        PenToolActive = true;
    }

    [RelayCommand]
    private void SetSideFront()
    {
        ActiveSideView = DesignerSideView.Front;

        // Priority 6 fix: switching via these toolbar buttons is a second, separate way
        // to change which side is being worked on, alongside clicking directly on a
        // card in the canvas (which already sets FocusedSide itself in
        // CardCanvasView.OnPointerPressed). Previously only the click path updated
        // FocusedSide, so choosing "Front" or "Back" from the toolbar changed what was
        // *shown* without changing what Layers/Properties/insert commands considered
        // *focused* -- e.g. switching to Back via this button and immediately inserting
        // an element would silently add it to whichever side was last clicked, not the
        // side now visible. Both must always stay in sync for a single-side view.
        FocusedSide = CardSide.Front;
    }

    [RelayCommand]
    private void SetSideBack()
    {
        ActiveSideView = DesignerSideView.Back;
        FocusedSide = CardSide.Back;
    }

    [RelayCommand]
    private void SetSideBoth() => ActiveSideView = DesignerSideView.Both;

    [RelayCommand]
    private void ZoomIn() => Zoom = Math.Min(16.0, Math.Round(Zoom * 1.25, 2));

    [RelayCommand]
    private void ZoomOut() => Zoom = Math.Max(0.05, Math.Round(Zoom / 1.25, 2));

    [RelayCommand]
    private void ZoomReset() => Zoom = 1.0;

    public void RefreshHistoryFlags()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    [RelayCommand]
    private async Task Save() => await SaveInternal(isAutosave: false);

    /// <summary>Single source of truth for "what does saving actually do", used by both
    /// the manual Save command and autosave. These must never diverge: a prior version
    /// of this file had autosave call _repository.SaveAsync directly instead of routing
    /// through here, which meant a file-backed design (SourceFilePath set) still got a
    /// fresh database Template row created every autosave cycle even though manual Save
    /// correctly wrote back to the file -- exactly the "same file-backed card appearing
    /// as a duplicate database entry" bug this consolidation fixes for good.</summary>
    private async Task SaveInternal(bool isAutosave)
    {
        _autosaveCts?.Cancel();
        SaveStatusText = isAutosave ? "Autosaving..." : "Saving...";
        try
        {
            if (Document.SourceFilePath is { } filePath)
            {
                // File-backed design (opened via File > Import): Save writes back to
                // the same .infoid file it came from, never the database -- this is
                // the whole point of importing a file rather than a Save-As-you-go-into
                // the-database model. No thumbnail-by-templateId either, since there's
                // no Template row; RecentFilesService.TrackAsync below is what lets
                // Recent Cards show file-backed designs at all.
                await _infoIdFileService.ExportAsync(Document, filePath);
                IsDirty = false;
                SaveStatusText = isAutosave ? "Autosaved" : "Saved";
                await _recentFilesService.TrackAsync(filePath, Document.Name);
            }
            else
            {
                var templateId = await _repository.SaveAsync(Document);
                IsDirty = false;
                SaveStatusText = isAutosave ? "Autosaved" : "Saved";

                try
                {
                    await _thumbnailService.GenerateThumbnailAsync(Document, templateId);
                }
                catch (Exception thumbnailEx)
                {
                    // A thumbnail render failure must never make an otherwise-successful
                    // save look failed to the user -- the Recent Cards/Templates list just
                    // falls back to no image for this one design; log and move on.
                    System.Diagnostics.Debug.WriteLine($"Thumbnail generation failed: {thumbnailEx}");
                }
            }
        }
        catch (Exception ex)
        {
            SaveStatusText = isAutosave ? "Autosave failed" : "Save failed";
            System.Diagnostics.Debug.WriteLine($"Card design save failed: {ex}");
        }
    }

    [RelayCommand]
    private async Task SaveVersion()
    {
        if (Document.SourceFilePath is not null)
        {
            // File-backed designs (File > Import) have no database Template row to
            // attach a version snapshot to -- Version History is a database-backed
            // feature (Part 60). Without this guard, the templateId lookup below would
            // stay null forever (Save() for a file-backed document never sets
            // PersistedTemplateId) and ".Value" would throw.
            return;
        }

        if (PersistedTemplateId(Document) is not { } templateId) { await Save(); templateId = PersistedTemplateId(Document)!.Value; }
        Document.VersionNumber++;
        await _repository.SaveVersionSnapshotAsync(templateId, Document, changeNote: null);
        await Save();
    }

    private static long? PersistedTemplateId(CardDesignDocument document) => document.PersistedTemplateId;

    /// <summary>Debounced autosave (Part 32) -- restarts a 2-second timer on every
    /// meaningful change instead of saving on every keystroke/drag frame. Runs on a
    /// background delay via Task.Delay, then marshals the actual save back onto the UI
    /// thread since it touches ObservableProperty state. No-ops entirely when the user
    /// has turned autosave off (Part 7) -- MarkDirty still sets IsDirty/SaveStatusText
    /// so the tab's "*" and Save button keep working normally either way.</summary>
    private void ScheduleAutosave()
    {
        if (!AutosaveEnabled) return;

        _autosaveCts?.Cancel();
        _autosaveCts = new CancellationTokenSource();
        _ = AutosaveAfterDelayAsync(_autosaveCts.Token);
    }

    private async Task AutosaveAfterDelayAsync(System.Threading.CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        if (token.IsCancellationRequested) return;

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => SaveInternal(isAutosave: true));
    }

    public void MarkDirty()
    {
        IsDirty = true;
        Document.UpdatedAt = DateTime.UtcNow;
        SaveStatusText = "Unsaved changes";
        ScheduleAutosave();
    }
}

/// <summary>Which ruler a guide-drag originated from -- Horizontal ruler (top) produces
/// a Vertical guide line (constant X); Vertical ruler (left) produces a Horizontal
/// guide line (constant Y). Named after the resulting guide's orientation, not the
/// ruler's, to match how CardCanvasView/RulerView consume it.</summary>
public enum GuideOrientation { Vertical, Horizontal }