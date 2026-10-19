using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Services;
using InfoID.Desktop.Features.CardDesigner.History;
using InfoID.Desktop.Features.CardDesigner.Models;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Database.Services;
using InfoID.Desktop.Features.Printing.Services;
using InfoID.Desktop.Features.Printing.ViewModels;
using InfoID.Desktop.Features.Database.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
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
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<CardDesignTabViewModel>();

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
    private readonly IPrinterService _printerService;
    private readonly IPrintStatusStore _printStatusStore;
    private readonly ICardFormatCatalogService _cardFormatCatalogService;
    private readonly MySqlSshTunnelService _mySqlSshTunnelService = new();
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
        IFaceDetectionService faceDetectionService, IPrinterService printerService,
        IPrintStatusStore printStatusStore, ICardFormatCatalogService cardFormatCatalogService)
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
        _printerService = printerService;
        _printStatusStore = printStatusStore;
        _cardFormatCatalogService = cardFormatCatalogService;
        PreviewRecords = new ObservableCollection<PreviewRecord>(previewDataProvider.GetSampleRecords());
        SelectedPreviewRecord = PreviewRecords.FirstOrDefault();
        Title = document.Name;
        _autosaveEnabled = preferences.Current.AutosaveEnabled;

        HookSide(Document.Front);
        HookSide(Document.Back);
        RefreshBackgroundPreview();
        RefreshLayerTree();
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
            RefreshLayerTree();
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

        // Rebuilding the whole Layers tree on every property change would mean doing it
        // on every pixel of a canvas drag (X/Y churn constantly) -- only rebuild for the
        // properties that can actually change what the panel shows.
        if (e.PropertyName is nameof(DesignerElement.Name) or nameof(DesignerElement.Visible)
            or nameof(DesignerElement.Locked) or nameof(DesignerElement.GroupId) or nameof(DesignerElement.ZIndex))
        {
            RefreshLayerTree();
        }
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

    private string _activeDatabaseTableName = string.Empty;

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
    public ObservableCollection<DatabaseTableDefinition> DatabaseTables { get; } = new();

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
        SaveDatabaseRows(includeCurrentRow: true);
        var previewViewModel = new PrintPreviewViewModel(Document, _evaluator, _previewDataProvider, _assetService);
        await _dialogService.ShowDialogAsync<PrintPreviewViewModel, bool>(previewViewModel);
    }

    /// <summary>Opens the real Print module (printer selection, bulk printing,
    /// rendering options, per-record print-status tracking) -- deliberately a separate
    /// dialog/command from Print Preview above, which stays exactly what its own doc
    /// comment says it is: appearance-only, no printer/copies/print action.</summary>
    [RelayCommand]
    //private async Task ShowPrintDialog()
    //{
    //    var printViewModel = new PrintDialogViewModel(
    //        Document, _printerService, _printStatusStore, _cardFormatCatalogService,
    //        _previewDataProvider, _evaluator, _assetService, _filePicker);
    //    await _dialogService.ShowDialogAsync<PrintDialogViewModel, bool>(printViewModel);
    //}
    private async Task ShowPrintDialog()
    {
        // Rows typed into the Internal Database grid get their real IDs before
        // printing, so print history is recorded against the saved records.
        SaveDatabaseRows(includeCurrentRow: true);

        // Scan every photo folder used on the card before rendering starts.
        foreach (var image in Document.Front.Elements.Concat(Document.Back.Elements).OfType<ImageElement>())
        {
            if (image.UsePhotoFolder && !string.IsNullOrWhiteSpace(image.PhotoFolderPath))
                await PhotoFolderService.Shared.PrepareFolderAsync(image.PhotoFolderPath);
        }

        var printViewModel = new PrintDialogViewModel(
            Document, _printerService, _printStatusStore, _cardFormatCatalogService,
            _previewDataProvider, _evaluator, _assetService, _filePicker,
            databaseRecords: BuildPrintRecords(),
            resolveImagePath: ResolvePrintImagePath,
            // Licensing check + card counting inside Print (FR-LIC-12). Resolved via
            // App.Services, same as AssetReferenceToBitmapConverter, rather than adding
            // another parameter to this tab's already long constructor chain.
            licenseScopes: InfoID.Desktop.App.Services.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory))
                as Microsoft.Extensions.DependencyInjection.IServiceScopeFactory,
            // Trial mode: who may print (license / trial / log in first) and the watermark.
            printAuthorization: InfoID.Desktop.App.Services.GetService(typeof(InfoID.Desktop.Features.Trial.Services.PrintAuthorizationService))
                as InfoID.Desktop.Features.Trial.Services.PrintAuthorizationService,
            trialState: InfoID.Desktop.App.Services.GetService(typeof(InfoID.Desktop.Features.Trial.Services.TrialModeState))
                as InfoID.Desktop.Features.Trial.Services.TrialModeState,
            // Visitor badges need a valid license but don't use up the card allowance.
            licensedFeature: _activeDatabaseType == DatabaseType.Visitors
                ? InfoID.Application.Services.LicensedFeatures.VisitorBadges
                : InfoID.Application.Services.LicensedFeatures.Printing);

        await _dialogService.ShowDialogAsync<PrintDialogViewModel, bool>(printViewModel);
    }


    /// <summary>Grid rows as print records. Each is labelled "Table/ID" (e.g.
    /// "Employees/1001") so print status is remembered per employee.</summary>
    private IReadOnlyList<PreviewRecord>? BuildPrintRecords()
    {
        if (DatabaseData is null)
            return null;

        var visibleRows = GetVisibleDatabaseRows().Select(v => v.Row).ToList();
        var tickedRows = visibleRows.Where(_checkedDatabaseRows.Contains).ToList();
        var rows = tickedRows.Count > 0 ? tickedRows : visibleRows;

        var columnNames = DatabaseData.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList();
        var primaryKey = DatabaseTables.LastOrDefault()?.PrimaryKey;
        var keyColumn = !string.IsNullOrWhiteSpace(primaryKey) && DatabaseData.Columns.Contains(primaryKey)
            ? primaryKey
            : PhotoFolderService.Shared.SuggestKeyColumn(columnNames);

        var tableName = string.IsNullOrWhiteSpace(_activeDatabaseTableName) ? "Data" : _activeDatabaseTableName;

        var records = new List<PreviewRecord>();

        // BLOB photo columns: RowToRecord turns bytes into the useless text
        // "System.Byte[]", so for printing each BLOB cell is swapped for a cached image
        // file path that the print renderer can load like any other photo.
        var blobColumns = DatabaseData.Columns.Cast<DataColumn>()
            .Where(c => c.DataType == typeof(byte[]))
            .ToList();

        foreach (var row in rows)
        {
            var fields = RowToRecord(row);

            foreach (var blobColumn in blobColumns)
            {
                fields[blobColumn.ColumnName] = ResolveDatabaseImageValue(row[blobColumn]) ?? string.Empty;
            }

            var key = keyColumn is not null &&
                      fields.TryGetValue(keyColumn, out var value) &&
                      !string.IsNullOrWhiteSpace(value)
                ? value
                : $"Row {DatabaseData.Rows.IndexOf(row) + 1}";

            records.Add(new PreviewRecord($"{tableName}/{key}", fields));
        }

        return records;
    }

    /// <summary>Photo for one printed card. null = the element's normal image,
    /// "" = print nothing (a placeholder box must never appear on a real card).
    /// Two per-record sources are supported, in this order: a photo folder
    /// ("Use photos from a folder"), then a database image column (the image element
    /// bound to a grid column such as "Photo"). Previously only the photo folder was
    /// handled, so an image bound to a database column printed the same design-time
    /// picture on every card.</summary>
    private string? ResolvePrintImagePath(ImageElement image, IReadOnlyDictionary<string, string> fields)
    {
        string? path;

        if (image.UsePhotoFolder && !string.IsNullOrWhiteSpace(image.PhotoFolderPath))
        {
            var service = PhotoFolderService.Shared;
            var key = service.BuildKey(image.PhotoFileNamePattern, fields);
            path = key is null ? null : service.FindPhoto(image.PhotoFolderPath, key);
        }
        else if (image.IsDatabaseBound && !string.IsNullOrWhiteSpace(image.DatabaseColumn))
        {
            fields.TryGetValue(image.DatabaseColumn, out var cellValue);
            path = ResolveDatabaseImageValue(cellValue);
        }
        else
        {
            return null;
        }

        if (path is not null)
            return path;

        return image.MissingPhotoBehavior == MissingPhotoBehavior.DefaultImage
            ? _assetService.ResolveToFullPath(image.AssetReference) ?? string.Empty
            : string.Empty;
    }

    private static readonly string DatabaseImageCacheDirectory =
        Path.Combine(Path.GetTempPath(), "InfoID", "DatabaseImageCache");

    /// <summary>BLOB bytes already written to the cache, keyed by the byte[] instance
    /// so canvas redraws don't re-hash the same photo every frame.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], string> BlobPathCache = new();

    /// <summary>Turns one database image cell into a loadable file path, or null if
    /// the cell has no usable image. Accepts the same three forms the grid's photo
    /// cells already accept (see GetDatabaseImage): BLOB bytes, an InfoID asset
    /// reference, or a plain file path.</summary>
    private string? ResolveDatabaseImageValue(object? value)
    {
        try
        {
            switch (value)
            {
                case byte[] bytes when bytes.Length > 0:
                    return CacheBlobImage(bytes);

                case string text when !string.IsNullOrWhiteSpace(text):
                    var trimmed = text.Trim();
                    var assetPath = _assetService.ResolveToFullPath(trimmed);
                    if (assetPath is not null && File.Exists(assetPath))
                        return assetPath;
                    return File.Exists(trimmed) ? Path.GetFullPath(trimmed) : null;

                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Database image resolve error", ex);
            return null;
        }
    }

    /// <summary>Writes BLOB image bytes to a content-addressed temp file (same bytes,
    /// same file) so both the canvas and the print renderer can load them by path.</summary>
    private static string? CacheBlobImage(byte[] bytes)
    {
        if (BlobPathCache.TryGetValue(bytes, out var cached) && File.Exists(cached))
            return cached;

        Directory.CreateDirectory(DatabaseImageCacheDirectory);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var path = Path.Combine(DatabaseImageCacheDirectory, hash + ".img");

        if (!File.Exists(path))
            File.WriteAllBytes(path, bytes);

        BlobPathCache.AddOrUpdate(bytes, path);
        return path;
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
        RefreshLayerTree();
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
    //public string ResolveDisplayText(string rawPlaceholderText, string bindingExpression)
    //{
    //    if (!PreviewModeEnabled) return rawPlaceholderText;
    //    var record = SelectedPreviewRecord?.Fields ?? EmptyRecord;
    //    return _evaluator.Evaluate(bindingExpression, record);
    //}

    public string ResolveDisplayText(
      string rawPlaceholderText,
      string bindingExpression)
    {
        if (!PreviewModeEnabled)
            return rawPlaceholderText;

        // Database preview takes priority when a database row is selected.
        if (SelectedDatabaseRow is not null)
        {
            var databaseValues =
                new Dictionary<string, string>();

            foreach (DataColumn column in SelectedDatabaseRow.Row.Table.Columns)
            {
                databaseValues[column.ColumnName] =
                    SelectedDatabaseRow.Row[column] == DBNull.Value
                        ? string.Empty
                        : SelectedDatabaseRow.Row[column]?.ToString()
                          ?? string.Empty;
            }

            // Direct database field binding.
            if (databaseValues.TryGetValue(
                    bindingExpression,
                    out var directValue))
            {
                return directValue;
            }

            // Existing expression-based database binding.
            return _evaluator.Evaluate(
                bindingExpression,
                databaseValues);
        }

        // Existing normal preview behavior.
        var record =
            SelectedPreviewRecord?.Fields ?? EmptyRecord;

        return _evaluator.Evaluate(
            bindingExpression,
            record);
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
    public bool ShowCommonTransformFields => PrimarySelection is not ImageElement;

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
        OnPropertyChanged(nameof(ShowCommonTransformFields));
        RefreshLayerSelectionHighlight();
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
            Name = "Image",
            AssetReference = result.AssetReference,
            X = 10,
            Y = 10,
            Width = result.WidthMm,
            Height = result.HeightMm,
        });
    }

    [RelayCommand]
    private async Task InsertPhoto()
    {
        var picked = await PickAndImportAsync("Insert Photo", defaultWidthMm: 20);
        if (picked is not { } result) return;
        InsertElement(new PhotoElement
        {
            Name = "Photo",
            AssetReference = result.AssetReference,
            X = 10,
            Y = 10,
            Width = result.WidthMm,
            Height = result.HeightMm,
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
            Name = "Image",
            AssetReference = assetReference,
            X = 10,
            Y = 10,
            Width = widthMm,
            Height = heightMm,
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
            Name = "Signature",
            AssetReference = result.AssetReference,
            X = 10,
            Y = 10,
            Width = result.WidthMm,
            Height = result.HeightMm,
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
            Name = "Line",
            Kind = ShapeKind.Line,
            FillEnabled = false,
            StrokeColorHex = "#1F2328",
            StrokeWidth = 1,
            X = minX,
            Y = minY,
            Width = Math.Max(width, 0.5),
            Height = Math.Max(height, 0.5),
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
            Name = "Drawing",
            X = minX,
            Y = minY,
            Width = boxWidth,
            Height = boxHeight,
            StrokeColorHex = "#1F2328",
            StrokeWidth = 1.2,
        };
        foreach (var p in pointsMm)
        {
            element.PointsFraction.Add(new PenPoint((p.X - minX) / boxWidth, (p.Y - minY) / boxHeight));
        }

        InsertElement(element);
    }

    // ------------------------------------------------------------- layers panel ----

    /// <summary>Grouped, ordered view of FocusedSideModel.Elements for the Layers
    /// panel -- a persistent collection whose CONTENTS get replaced (not the property
    /// itself) by RefreshLayerTree(), so the panel's ItemsControl reacts via ordinary
    /// INotifyCollectionChanged without needing a property-changed notification on
    /// every rebuild.</summary>
    public ObservableCollection<LayerNode> LayerTree { get; } = new();

    /// <summary>Expand/collapse state survives RefreshLayerTree() rebuilding every
    /// LayerNode instance from scratch -- keyed by GroupId since that's the one stable
    /// identity a group has across rebuilds (a fresh LayerNode object is created for it
    /// every time, so the node itself can't remember its own prior expanded state).</summary>
    private readonly Dictionary<string, bool> _groupExpansion = new();

    /// <summary>Rebuilds LayerTree from scratch: one top-level LayerNode per ungrouped
    /// element or per distinct GroupId, ordered front-most (highest ZIndex) first --
    /// the standard "top of the layers list = what's drawn on top" convention. A
    /// group's own position in that order is its highest member's ZIndex, so dragging
    /// any one member of a group to the front visually brings the whole group's row
    /// up too.</summary>
    private void RefreshLayerTree()
    {
        LayerTree.Clear();

        var elements = FocusedSideModel.Elements;
        var groupNames = FocusedSideModel.GroupNames;
        var seenGroups = new HashSet<string>();
        var topLevel = new List<(int SortKey, LayerNode Node)>();

        foreach (var element in elements)
        {
            if (!string.IsNullOrEmpty(element.GroupId))
            {
                if (!seenGroups.Add(element.GroupId)) continue;

                var members = elements.Where(e => e.GroupId == element.GroupId)
                    .OrderByDescending(e => e.ZIndex)
                    .ToList();
                var name = groupNames.TryGetValue(element.GroupId, out var named) ? named : $"Group ({members.Count})";
                var groupNode = new LayerNode(element.GroupId, name, members)
                {
                    IsExpanded = !_groupExpansion.TryGetValue(element.GroupId, out var expanded) || expanded,
                };
                foreach (var member in members) groupNode.Children.Add(new LayerNode(member));
                topLevel.Add((members.Max(e => e.ZIndex), groupNode));
            }
            else
            {
                topLevel.Add((element.ZIndex, new LayerNode(element)));
            }
        }

        // Empty folders: named via "+" (CreateLayerGroup) but nothing dragged into them
        // yet. They have no ZIndex to sort by, so they always sit above every element --
        // an empty folder waiting to be filled reads better at the top than buried in
        // the middle of a z-order it isn't actually part of.
        var emptySortKey = (elements.Count == 0 ? 0 : elements.Max(e => e.ZIndex)) + groupNames.Count;
        foreach (var (groupId, name) in groupNames)
        {
            if (!seenGroups.Add(groupId)) continue;

            var groupNode = new LayerNode(groupId, name, System.Array.Empty<DesignerElement>())
            {
                IsExpanded = !_groupExpansion.TryGetValue(groupId, out var expanded) || expanded,
            };
            topLevel.Add((emptySortKey--, groupNode));
        }

        foreach (var (_, node) in topLevel.OrderByDescending(t => t.SortKey))
        {
            LayerTree.Add(node);
        }

        RefreshLayerSelectionHighlight();
    }

    private void RefreshLayerSelectionHighlight()
    {
        foreach (var node in LayerTree)
        {
            if (node.IsGroup)
            {
                foreach (var child in node.Children) child.IsSelected = SelectedElements.Contains(child.Element!);
                node.IsSelected = node.Children.Any(c => c.IsSelected);
            }
            else
            {
                node.IsSelected = SelectedElements.Contains(node.Element!);
            }
        }
    }

    [RelayCommand]
    private void SelectLayerNode(LayerNode node)
    {
        if (node.IsGroup)
        {
            if (node.Children.Count > 0) SelectOnly(node.Children[0].Element);
            return;
        }

        SelectOnly(node.Element);
    }

    [RelayCommand]
    private void ToggleLayerNodeSelection(LayerNode node)
    {
        if (node.IsGroup)
        {
            if (node.Children.Count > 0) ToggleSelection(node.Children[0].Element!);
            return;
        }

        ToggleSelection(node.Element!);
    }

    [RelayCommand]
    private void ToggleGroupExpanded(LayerNode node)
    {
        if (!node.IsGroup) return;
        node.IsExpanded = !node.IsExpanded;
        _groupExpansion[node.GroupId!] = node.IsExpanded;
    }

    /// <summary>"+" button next to the Layers header (Part 96 follow-up): creates an
    /// empty, named folder up front -- matching the reference app's "Front / Color-Black
    /// / UV" structure, where a folder is a real, nameable thing you create and then
    /// populate, not just an incidental side-effect of multi-selecting elements and
    /// clicking Group(). Default name is "Layer N" rather than "Group N" -- "Group"
    /// read as confusing next to the individual element rows underneath it.</summary>
    [RelayCommand]
    private void CreateLayerGroup()
    {
        var groupId = Guid.NewGuid().ToString("N");
        FocusedSideModel.GroupNames[groupId] = $"Layer {FocusedSideModel.GroupNames.Count + 1}";
        _groupExpansion[groupId] = true;
        NotifyDocumentChanged();
        MarkDirty();
        RefreshLayerTree();
    }

    [RelayCommand]
    private void BeginRenameGroup(LayerNode node)
    {
        if (!node.IsGroup) return;
        node.RenameText = node.DisplayName;
        node.IsRenaming = true;
    }

    /// <summary>Commits the folder's new name, or silently cancels if it's empty/
    /// whitespace-only -- same "never leave it blank" rule CommitRename uses for the tab
    /// title.</summary>
    [RelayCommand]
    private void CommitRenameGroup(LayerNode node)
    {
        if (node.IsGroup)
        {
            var trimmed = node.RenameText.Trim();
            if (trimmed.Length > 0 && trimmed != node.DisplayName)
            {
                FocusedSideModel.GroupNames[node.GroupId!] = trimmed;
                NotifyDocumentChanged();
                MarkDirty();
            }
        }

        node.IsRenaming = false;
        RefreshLayerTree();
    }

    [RelayCommand]
    private void CancelRenameGroup(LayerNode node) => node.IsRenaming = false;

    [RelayCommand]
    private void ToggleLayerNodeVisible(LayerNode node)
    {
        var members = node.IsGroup ? node.Children.Select(c => c.Element!).ToList() : new List<DesignerElement> { node.Element! };
        var newValue = !members.Any(m => m.Visible);
        foreach (var m in members) m.Visible = newValue;
        NotifyDocumentChanged();
        MarkDirty();
    }

    [RelayCommand]
    private void ToggleLayerNodeLocked(LayerNode node)
    {
        var members = node.IsGroup ? node.Children.Select(c => c.Element!).ToList() : new List<DesignerElement> { node.Element! };
        var newValue = !members.All(m => m.Locked);
        foreach (var m in members)
        {
            m.Locked = newValue;
            if (newValue) SelectedElements.Remove(m);
        }
        NotifySelectionChanged();
        NotifyDocumentChanged();
        MarkDirty();
    }

    /// <summary>Deleting a group folder deletes its contents too (matching most layer
    /// panels' default "delete group" behavior) and removes the folder name itself --
    /// otherwise an empty-but-still-named entry in GroupNames would make the folder
    /// reappear as an empty one on the very next RefreshLayerTree().</summary>
    [RelayCommand]
    private void DeleteLayerNode(LayerNode node)
    {
        if (node.IsGroup)
        {
            var members = node.Children.Select(c => c.Element!).ToList();
            if (members.Count > 0)
            {
                History.Execute(new DeleteElementsCommand(FocusedSideModel, members));
                foreach (var m in members) SelectedElements.Remove(m);
                NotifySelectionChanged();
            }

            FocusedSideModel.GroupNames.Remove(node.GroupId!);
            _groupExpansion.Remove(node.GroupId!);
        }
        else
        {
            History.Execute(new DeleteElementsCommand(FocusedSideModel, new[] { node.Element! }));
            SelectedElements.Remove(node.Element!);
            NotifySelectionChanged();
        }

        RefreshHistoryFlags();
        MarkDirty();
        RefreshLayerTree();
    }

    /// <summary>Drives the Layers panel's drag-and-drop: dropping a leaf onto a group
    /// folder (or onto any member of one) joins that folder; dropping a leaf onto
    /// another ungrouped leaf reorders it (ungrouping it first, if it was grouped);
    /// dragging a whole group reorders its members as one block next to the drop
    /// target, without nesting one group inside another. Both the z-order change and
    /// the folder-membership change go through History, so a drag -- like every other
    /// layer move -- is a single undoable step.</summary>
    public void HandleLayerDrop(LayerNode dragged, LayerNode target)
    {
        if (ReferenceEquals(dragged, target)) return;

        if (dragged.IsGroup)
        {
            var targetZIndex = target.IsGroup
                ? (target.Children.Count > 0 ? target.Children.Max(c => c.Element!.ZIndex) : (int?)null)
                : target.Element!.ZIndex;
            if (targetZIndex is { } z) ReorderZIndexBlock(dragged.Children.Select(c => c.Element!).ToList(), z);
        }
        else
        {
            var element = dragged.Element!;
            string? newGroupId = target.IsGroup ? target.GroupId
                : !string.IsNullOrEmpty(target.Element!.GroupId) ? target.Element.GroupId
                : null;

            if (newGroupId != element.GroupId)
            {
                History.Execute(new ChangePropertyCommand<DesignerElement, string?>(
                    element, (el, v) => el.GroupId = v, element.GroupId, newGroupId, "Move layer"));
                RefreshHistoryFlags();
            }

            if (newGroupId is null && !target.IsGroup)
            {
                ReorderZIndexBlock(new[] { element }, target.Element!.ZIndex);
            }
        }

        NotifyDocumentChanged();
        MarkDirty();
        RefreshLayerTree();
    }

    /// <summary>Dropping onto the blank area below the layer list (not onto any row)
    /// pulls a grouped element back out to the top level -- the explicit "let go of the
    /// group" gesture the panel was missing, for when dragging directly onto an
    /// ungrouped sibling row isn't handy/visible on screen.</summary>
    public void HandleLayerDropToTopLevel(LayerNode dragged)
    {
        if (dragged.IsGroup || dragged.Element is not { } element || element.GroupId is null) return;

        History.Execute(new ChangePropertyCommand<DesignerElement, string?>(
            element, (el, v) => el.GroupId = v, element.GroupId, null, "Move layer out of group"));
        RefreshHistoryFlags();
        NotifyDocumentChanged();
        MarkDirty();
        RefreshLayerTree();
    }

    /// <summary>Renumbers every element on the focused side so movingElements sit
    /// immediately above targetZIndex, preserving everyone else's relative order --
    /// through History as one CompositeCommand of ReorderElementCommands, so a drag
    /// reorder undoes/redoes as a single step exactly like Bring Forward/Send Backward
    /// already do (see Reorder() below). ZIndex is only ever compared relatively
    /// elsewhere in this codebase (rendering order, front/back commands), so renumbering
    /// it densely from 0 is safe -- nothing relies on the specific gaps between values.</summary>
    private void ReorderZIndexBlock(IReadOnlyList<DesignerElement> movingElements, int targetZIndex)
    {
        var others = FocusedSideModel.Elements.Where(e => !movingElements.Contains(e)).OrderBy(e => e.ZIndex).ToList();
        var insertAt = others.FindIndex(e => e.ZIndex == targetZIndex);
        insertAt = insertAt < 0 ? others.Count : insertAt + 1;

        var ordered = new List<DesignerElement>(others);
        ordered.InsertRange(insertAt, movingElements);

        var commands = new List<IDesignCommand>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].ZIndex != i) commands.Add(new ReorderElementCommand(ordered[i], ordered[i].ZIndex, i));
        }

        if (commands.Count == 0) return;

        History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Reorder layers", commands));
        RefreshHistoryFlags();
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

    //[RelayCommand]
    //private async Task AddDatabase()
    //{
    //    var dialog = new ConnectDatabaseDialogViewModel(_filePicker);

    //    await _dialogService.ShowDialogAsync<ConnectDatabaseDialogViewModel, bool>(dialog);
    //}

    public DataTable? DatabaseData { get; private set; }
    public ObservableCollection<string> DatabaseColumns { get; } = new();
    public DataView? DatabaseDataView => DatabaseData?.DefaultView;


    public bool HasDatabaseConnection => DatabaseData is not null;

    /// <summary>Every AddDatabase branch already raises PropertyChanged for
    /// DatabaseData after loading, so HasDatabaseConnection is refreshed here instead
    /// of editing each branch.</summary>
    //protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    //{
    //    base.OnPropertyChanged(e);

    //    if (e.PropertyName == nameof(DatabaseData))
    //        base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(HasDatabaseConnection)));
    //}
    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(DatabaseData))
        {
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(HasDatabaseConnection)));

            // Search: rebuild the column dropdown when the columns change
            RefreshSearchColumns();

            // Checkboxes: forget ticks that belong to a previous table (new connection / disconnect)
            _checkedDatabaseRows.RemoveWhere(r => r.Table != DatabaseData);
            DatabaseRowChecksChanged?.Invoke(this, EventArgs.Empty);

            // Saving grid edits: follow the new table's changes.
            OnDatabaseTableReplaced();
        }

        // Navigation: keep the "5 of 12" text up to date
        if (e.PropertyName is nameof(DatabaseData) or nameof(DatabaseDataView) or nameof(SelectedDatabaseRow))
            base.OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(RecordPositionText)));
    }

    /// <summary>Rows ticked with the grid's checkbox column. Separate from
    /// SelectedDatabaseRow, which is the "current" row driving the card preview.</summary>
    private readonly HashSet<DataRow> _checkedDatabaseRows = new();

    /// <summary>Raised whenever ticks change, so the grid can refresh visible checkboxes.</summary>
    public event EventHandler? DatabaseRowChecksChanged;

    public bool IsDatabaseRowChecked(DataRow row) => _checkedDatabaseRows.Contains(row);

    public bool AreAllVisibleDatabaseRowsChecked
    {
        get
        {
            var rows = GetVisibleDatabaseRows();
            return rows.Count > 0 && rows.All(r => _checkedDatabaseRows.Contains(r.Row));
        }
    }

    public void SetDatabaseRowChecked(DataRow row, bool isChecked)
    {
        if (isChecked) _checkedDatabaseRows.Add(row);
        else _checkedDatabaseRows.Remove(row);

        DatabaseRowChecksChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Header checkbox: ticks or unticks every row currently visible
    /// (respects an active search).</summary>
    public void SetAllDatabaseRowsChecked(bool isChecked)
    {
        foreach (var rowView in GetVisibleDatabaseRows())
        {
            if (isChecked) _checkedDatabaseRows.Add(rowView.Row);
            else _checkedDatabaseRows.Remove(rowView.Row);
        }

        DatabaseRowChecksChanged?.Invoke(this, EventArgs.Empty);
    }
    public ObservableCollection<DatabaseField> DatabaseFields { get; } = new();

    [ObservableProperty]
    private DataRowView? _selectedDatabaseRow;

    //partial void OnSelectedDatabaseRowChanged(DataRowView? value)
    //{
    //    NotifyDocumentChanged();
    //}

    //[RelayCommand]
    //private void AddDatabaseField(DatabaseField? field)
    //{
    //    if (field is null)
    //        return;

    //    if (PrimarySelection is not TextElement textElement)
    //        return;

    //    textElement.DatabaseColumn = field.ColumnName;
    //    textElement.IsDatabaseBound = true;

    //    NotifyDocumentChanged();
    //    MarkDirty();
    //}

    [RelayCommand]
    private void AddDatabaseField(DatabaseField? field)
    {
        if (field is null)
            return;

        if (PrimarySelection is TextElement textElement)
        {
            textElement.DatabaseColumn =
                field.ColumnName;

            textElement.IsDatabaseBound = true;
        }
        else if (PrimarySelection is ImageElement imageElement)
        {
            imageElement.DatabaseColumn =
                field.ColumnName;

            imageElement.IsDatabaseBound = true;
        }
        else
        {
            return;
        }

        NotifyDocumentChanged();
        MarkDirty();
    }
    public ObservableCollection<DatabaseRowValue> SelectedDatabaseValues { get; } = new();

    partial void OnSelectedDatabaseRowChanged(DataRowView? value)
    {
        SelectedDatabaseValues.Clear();

        if (value is null)
        {
            NotifySelectionChanged();
            return;
        }

        foreach (DataColumn column in value.Row.Table.Columns)
        {
            SelectedDatabaseValues.Add(new DatabaseRowValue
            {
                ColumnName = column.ColumnName,
                Value = value.Row[column]?.ToString() ?? string.Empty
            });
        }

        // A database row selection is separate from canvas-element selection.
        //SelectedElements.Clear();

        //NotifySelectionChanged();
        NotifyDocumentChanged();
    }

    [RelayCommand]
    private async Task AddDatabase()
    {
        var selectionDialog = new DatabaseTypeSelectionDialogViewModel();

        var selectedType = await _dialogService.ShowDialogAsync<DatabaseTypeSelectionDialogViewModel, DatabaseType?>(selectionDialog);

        if (selectedType is null)
            return;

        // Switching to another table/source: save the grid's pending rows first.
        if (!await ConfirmLeaveDatabaseAsync("Switch table"))
            return;

        var previousData = DatabaseData;
        var previousTable = _activeDatabaseTableName;
        var previousType = _activeDatabaseType;

        // Which kind of source is being connected (only the Internal Database is
        // written back to from the grid). Set before connecting, so the grid is built
        // with the right editing rules; put back if nothing gets connected.
        _activeDatabaseType = selectedType.Value;

        _connectingDatabase = true;
        try
        {
            await ConnectDatabaseAsync(selectedType.Value);
        }
        finally
        {
            _connectingDatabase = false;
        }

        if (ReferenceEquals(previousData, DatabaseData) &&
            string.Equals(previousTable, _activeDatabaseTableName, StringComparison.Ordinal))
        {
            _activeDatabaseType = previousType; // cancelled
        }
        else if (DatabaseData is null)
        {
            _activeDatabaseType = null;
        }

        OnDatabaseTableReplaced();
    }

    /// <summary>The connection branches for each source type (unchanged; split out of
    /// AddDatabase so the type of whatever got connected can be recorded).</summary>
    private async Task ConnectDatabaseAsync(DatabaseType? selectedType)
    {
        if (selectedType == DatabaseType.Visitors)
        {
            await ConnectVisitorsAsync();
            return;
        }

        if (selectedType == DatabaseType.Excel)
        {
            var excelDialog =
                new ExcelConnectionDialogViewModel(_filePicker, _dialogService);

            var connection =
                await _dialogService.ShowDialogAsync<
                    ExcelConnectionDialogViewModel,
                    DatabaseConnection?>(excelDialog);

            if (connection is null)
                return;

            var designDialog =
                new DatabaseDesignDialogViewModel(
                    connection,
                    _dialogService);

            var tableDefinition =
                await _dialogService.ShowDialogAsync<
                    DatabaseDesignDialogViewModel,
                    DatabaseTableDefinition?>(
                        designDialog);

            if (tableDefinition is null)
                return;

            try
            {
                var excelService = new ExcelOdbcService();

                DatabaseData =
                    excelService.ReadSheet(
                        tableDefinition.ConnectionString,
                        tableDefinition.TableName);

                DatabaseTables.Add(tableDefinition);

                _activeDatabaseTableName =
                    tableDefinition.TableName;

                if (!string.IsNullOrWhiteSpace(
                        tableDefinition.PrimaryKey))
                {
                    DatabaseData.DefaultView.Sort =
                        $"[{tableDefinition.PrimaryKey}] ASC";
                }

                OnPropertyChanged(nameof(DatabaseData));
                OnPropertyChanged(nameof(DatabaseDataView));

                DatabaseFields.Clear();

                foreach (var column in tableDefinition.Columns)
                {
                    if (!DatabaseData.Columns.Contains(column.Name))
                        continue;

                    DatabaseFields.Add(
                        new DatabaseField
                        {
                            DatabaseName =
                                connection.Name,

                            TableName =
                                tableDefinition.TableName,

                            ColumnName =
                                column.Name
                        });
                }
            }
            catch (Exception ex)
            {
                Log.Error("Excel data error", ex);
            }

            return;
        }
        if (selectedType == DatabaseType.ODBC)
        {
            var odbcDialog =
                new OdbcConnectionDialogViewModel();

            var connection =
                await _dialogService.ShowDialogAsync<
                    OdbcConnectionDialogViewModel,
                    DatabaseConnection?>(odbcDialog);

            if (connection is null)
                return;

            var designDialog = new DatabaseDesignDialogViewModel(connection, _dialogService);

            var tableDefinition = await _dialogService.ShowDialogAsync<DatabaseDesignDialogViewModel, DatabaseTableDefinition?>(designDialog);

            if (tableDefinition is not null)
            {
                DatabaseTables.Add(tableDefinition);

                var databaseService =
                    new OdbcDatabaseService();

                try
                {
                    //DatabaseData =
                    //    databaseService.GetData(
                    //        tableDefinition.ConnectionString,
                    //        tableDefinition.TableName);

                    //OnPropertyChanged(nameof(DatabaseData));
                    //OnPropertyChanged(nameof(DatabaseDataView));

                    //DatabaseFields.Clear();

                    //foreach (DataColumn column in DatabaseData.Columns)
                    //{
                    //    DatabaseFields.Add(new DatabaseField
                    //    {
                    //        DatabaseName = connection.Name,
                    //        TableName = tableDefinition.TableName,
                    //        ColumnName = column.ColumnName
                    //    });
                    //}
                    DatabaseData =
    databaseService.GetData(
        tableDefinition.ConnectionString,
        tableDefinition.TableName);
                    if (!string.IsNullOrWhiteSpace(tableDefinition.PrimaryKey))
                    {
                        DatabaseData.DefaultView.Sort =
                            $"[{tableDefinition.PrimaryKey}] ASC";
                    }
                    // Add design-time columns that don't physically exist
                    // in the original database yet.
                    foreach (var definedColumn in tableDefinition.Columns)
                    {
                        if (!DatabaseData.Columns.Contains(definedColumn.Name))
                        {
                            DatabaseData.Columns.Add(
                                definedColumn.Name,
                                typeof(string));
                        }
                    }

                    OnPropertyChanged(nameof(DatabaseData));
                    OnPropertyChanged(nameof(DatabaseDataView));

                    DatabaseFields.Clear();

                    foreach (DataColumn column in DatabaseData.Columns)
                    {
                        DatabaseFields.Add(new DatabaseField
                        {
                            DatabaseName = connection.Name,
                            TableName = tableDefinition.TableName,
                            ColumnName = column.ColumnName
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Database data error", ex);
                }
            }
            return;
        }
        if (selectedType == DatabaseType.SQLite)
        {
            var sqliteDialog =
                new SqliteConnectionDialogViewModel(_filePicker);

            var connection =
                await _dialogService.ShowDialogAsync<
                    SqliteConnectionDialogViewModel,
                    DatabaseConnection?>(sqliteDialog);

            if (connection is null)
                return;

            var designDialog =
                new DatabaseDesignDialogViewModel(
                    connection,
                    _dialogService);

            var tableDefinition =
                await _dialogService.ShowDialogAsync<
                    DatabaseDesignDialogViewModel,
                    DatabaseTableDefinition?>(designDialog);

            if (tableDefinition is null)
                return;

            DatabaseTables.Add(tableDefinition);

            _activeDatabaseTableName =
                tableDefinition.TableName;

            var sqliteService =
                new SqliteDatabaseService();

            try
            {
                DatabaseData =
                    sqliteService.GetData(
                        tableDefinition.ConnectionString,
                        tableDefinition.TableName);

                if (!string.IsNullOrWhiteSpace(
                        tableDefinition.PrimaryKey))
                {
                    DatabaseData.DefaultView.Sort =
                        $"[{tableDefinition.PrimaryKey}] ASC";
                }

                OnPropertyChanged(
                    nameof(DatabaseData));

                OnPropertyChanged(
                    nameof(DatabaseDataView));

                DatabaseFields.Clear();

                foreach (var column in tableDefinition.Columns)
                {
                    if (!DatabaseData.Columns.Contains(
                            column.Name))
                    {
                        continue;
                    }

                    DatabaseFields.Add(
                        new DatabaseField
                        {
                            DatabaseName =
                                connection.Name,

                            TableName =
                                tableDefinition.TableName,

                            ColumnName =
                                column.Name
                        });
                }
            }
            catch (Exception ex)
            {
                Log.Error("SQLite data error", ex);
            }

            return;
        }
        if (selectedType == DatabaseType.SQLServer)
        {
            var sqlServerDialog =
                new SqlServerConnectionDialogViewModel();

            var connection =
                await _dialogService.ShowDialogAsync<
                    SqlServerConnectionDialogViewModel,
                    DatabaseConnection?>(sqlServerDialog);

            if (connection is null)
                return;

            var designDialog =
                new DatabaseDesignDialogViewModel(
                    connection,
                    _dialogService);

            var tableDefinition =
                await _dialogService.ShowDialogAsync<
                    DatabaseDesignDialogViewModel,
                    DatabaseTableDefinition?>(designDialog);

            if (tableDefinition is not null)
            {
                DatabaseTables.Add(tableDefinition);

                var databaseService =
                    new OdbcDatabaseService();

                try
                {
                    DatabaseData =
                        databaseService.GetData(
                            tableDefinition.ConnectionString,
                            tableDefinition.TableName);

                    foreach (var definedColumn in tableDefinition.Columns)
                    {
                        if (!DatabaseData.Columns.Contains(
                                definedColumn.Name))
                        {
                            DatabaseData.Columns.Add(
                                definedColumn.Name,
                                typeof(string));
                        }
                    }

                    OnPropertyChanged(nameof(DatabaseData));
                    OnPropertyChanged(nameof(DatabaseDataView));

                    DatabaseFields.Clear();

                    foreach (DataColumn column in DatabaseData.Columns)
                    {
                        DatabaseFields.Add(new DatabaseField
                        {
                            DatabaseName = connection.Name,
                            TableName = tableDefinition.TableName,
                            ColumnName = column.ColumnName
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("SQL Server data error", ex);
                }
            }

            return;
        }

        if (selectedType == DatabaseType.MySQL)
        {
            var mySqlDialog =
                new MySqlConnectionDialogViewModel(_filePicker, _mySqlSshTunnelService);

            var connection =
                await _dialogService.ShowDialogAsync<
                    MySqlConnectionDialogViewModel,
                    DatabaseConnection?>(mySqlDialog);

            if (connection is null)
                return;

            var designDialog =
                new DatabaseDesignDialogViewModel(
                    connection,
                    _dialogService);

            var tableDefinition =
                await _dialogService.ShowDialogAsync<
                    DatabaseDesignDialogViewModel,
                    DatabaseTableDefinition?>(designDialog);

            if (tableDefinition is not null)
            {
                DatabaseTables.Add(tableDefinition);

                var databaseService =
                    new MySqlDatabaseService();

                try
                {
                    DatabaseData =
                        databaseService.GetData(
                            tableDefinition.ConnectionString,
                            tableDefinition.TableName);

                    if (!string.IsNullOrWhiteSpace(
                            tableDefinition.PrimaryKey))
                    {
                        DatabaseData.DefaultView.Sort =
                            $"[{tableDefinition.PrimaryKey}] ASC";
                    }

                    OnPropertyChanged(
                        nameof(DatabaseData));

                    OnPropertyChanged(
                        nameof(DatabaseDataView));

                    DatabaseFields.Clear();

                    foreach (var column in tableDefinition.Columns)
                    {
                        if (!DatabaseData.Columns.Contains(
                                column.Name))
                        {
                            continue;
                        }

                        DatabaseFields.Add(
                            new DatabaseField
                            {
                                DatabaseName =
                                    connection.Name,

                                TableName =
                                    tableDefinition.TableName,

                                ColumnName =
                                    column.Name
                            });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("MySQL data error", ex);
                }
            }

            return;
        }

        if (selectedType == DatabaseType.Access)
        {
            var accessDialog =
                new AccessConnectionDialogViewModel(
                    _filePicker);

            var connection =
                await _dialogService.ShowDialogAsync<
                    AccessConnectionDialogViewModel,
                    DatabaseConnection?>(accessDialog);

            if (connection is null)
                return;

            try
            {
                var odbcService =
                    new OdbcDatabaseService();

                var tables =
                    odbcService.GetTables(
                        connection.ConnectionString);

                if (tables.Count == 0)
                {
                    Log.Debug("ACCESS: No tables found.");

                    return;
                }

                var tableName =
                    connection.SelectedTable;

                if (string.IsNullOrWhiteSpace(tableName))
                    return;

                var columns =
                    odbcService.GetColumnDefinitions(
                        connection.ConnectionString,
                        tableName);

                var data =
                    odbcService.GetData(
                        connection.ConnectionString,
                        tableName);

                DatabaseData = data;

                var tableDefinition =
                    new DatabaseTableDefinition
                    {
                        DatabaseName =
                            connection.Name,

                        TableName =
                            tableName,

                        ConnectionString =
                            connection.ConnectionString,

                        Columns =
                            columns.ToList()
                    };

                DatabaseTables.Add(tableDefinition);

                _activeDatabaseTableName =
                    tableName;

                OnPropertyChanged(
                    nameof(DatabaseData));

                OnPropertyChanged(
                    nameof(DatabaseDataView));

                DatabaseFields.Clear();

                foreach (var field in connection.SelectedColumns)
                {
                    DatabaseFields.Add(
                        new DatabaseField
                        {
                            DatabaseName =
                                connection.Name,

                            TableName =
                                tableName,

                            ColumnName =
                                field.Name
                        });
                }
            }
            catch (Exception ex)
            {
                Log.Error("Access data error", ex);
            }

            return;
        }
        if (selectedType == DatabaseType.PostgreSQL)
        {
            var postgreSqlDialog =
                new PostgreSqlConnectionDialogViewModel();

            var connection =
                await _dialogService.ShowDialogAsync<
                    PostgreSqlConnectionDialogViewModel,
                    DatabaseConnection?>(
                        postgreSqlDialog);

            if (connection is null)
                return;

            var designDialog =
                new DatabaseDesignDialogViewModel(
                    connection,
                    _dialogService);

            var tableDefinition =
                await _dialogService.ShowDialogAsync<
                    DatabaseDesignDialogViewModel,
                    DatabaseTableDefinition?>(
                        designDialog);

            if (tableDefinition is not null)
            {
                DatabaseTables.Add(tableDefinition);

                _activeDatabaseTableName =
                    tableDefinition.TableName;

                var databaseService =
                    new PostgreSqlDatabaseService();

                try
                {
                    DatabaseData =
                        databaseService.GetData(
                            tableDefinition.ConnectionString,
                            tableDefinition.TableName);

                    if (!string.IsNullOrWhiteSpace(
                            tableDefinition.PrimaryKey))
                    {
                        DatabaseData.DefaultView.Sort =
                            $"[{tableDefinition.PrimaryKey}] ASC";
                    }

                    OnPropertyChanged(nameof(DatabaseData));
                    OnPropertyChanged(nameof(DatabaseDataView));

                    DatabaseFields.Clear();

                    foreach (var column in tableDefinition.Columns)
                    {
                        if (!DatabaseData.Columns.Contains(
                                column.Name))
                            continue;

                        DatabaseFields.Add(
                            new DatabaseField
                            {
                                DatabaseName =
                                    connection.Name,

                                TableName =
                                    tableDefinition.TableName,

                                ColumnName =
                                    column.Name
                            });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("PostgreSQL data error", ex);
                }
            }

            return;
        }

        if (selectedType == DatabaseType.CSV ||
    selectedType == DatabaseType.TXT)
        {
            var csvDialog =
                new CsvTextConnectionDialogViewModel(
                    _filePicker);

            var connection =
                await _dialogService.ShowDialogAsync<
                    CsvTextConnectionDialogViewModel,
                    DatabaseConnection?>(csvDialog);

            if (connection is null)
                return;

            try
            {
                var csvService =
                    new CsvTextDatabaseService();

                DatabaseData =
                    csvService.LoadFile(
                        connection.ConnectionString);

                if (DatabaseData.Columns.Count == 0)
                {
                    Log.Debug("CSV/TXT file contains no columns.");

                    return;
                }

                var tableName =
                    System.IO.Path.GetFileNameWithoutExtension(
                        connection.ConnectionString);

                var primaryKey = DatabaseData.Columns
                    .Cast<DataColumn>()
                    .FirstOrDefault(column =>
                        column.ColumnName.Equals(
                            "ID",
                            StringComparison.OrdinalIgnoreCase));

                var tableDefinition =
                    new DatabaseTableDefinition
                    {
                        DatabaseName = connection.Name,
                        TableName = tableName,
                        ConnectionString =
                            connection.ConnectionString,
                        PrimaryKey =
                            primaryKey?.ColumnName ?? string.Empty,
                        Columns = DatabaseData.Columns
                            .Cast<DataColumn>()
                            .Select(column =>
                                new DatabaseColumn
                                {
                                    Name = column.ColumnName,
                                    DataType =
                                        column.DataType.Name,
                                    IsPrimaryKey =
                                        primaryKey?.ColumnName ==
                                        column.ColumnName,
                                    IsSelected = true
                                })
                            .ToList()
                    };

                DatabaseTables.Add(tableDefinition);

                if (!string.IsNullOrWhiteSpace(
                        tableDefinition.PrimaryKey))
                {
                    DatabaseData.DefaultView.Sort =
                        $"[{tableDefinition.PrimaryKey}] ASC";
                }

                OnPropertyChanged(
                    nameof(DatabaseData));

                OnPropertyChanged(
                    nameof(DatabaseDataView));

                DatabaseFields.Clear();

                foreach (DataColumn column in DatabaseData.Columns)
                {
                    DatabaseFields.Add(
                        new DatabaseField
                        {
                            DatabaseName =
                                connection.Name,
                            TableName =
                                tableName,
                            ColumnName =
                                column.ColumnName
                        });
                }

            }
            catch (Exception ex)
            {
                Log.Error("CSV/TXT data error", ex);

                Log.Warn("Could not load CSV/TXT file", ex);
            }

            return;
        }

        if (selectedType == DatabaseType.InternalDatabase)
        {
            var sqliteService =
      new InternalSqliteDatabaseService();

            sqliteService.Initialize();

            var connection = new DatabaseConnection
            {
                Name = "InfoID Internal Database",
                Type = DatabaseType.InternalDatabase,
                ConnectionString = sqliteService.ConnectionString,
                IsConnected = true
            };

            var designDialog =
                new DatabaseDesignDialogViewModel(
                    connection,
                    _dialogService);

            var tableDefinition =
                await _dialogService.ShowDialogAsync<
                    DatabaseDesignDialogViewModel,
                    DatabaseTableDefinition?>(designDialog);

            if (tableDefinition is not null)
            {
                DatabaseTables.Add(tableDefinition);
                _activeDatabaseTableName = tableDefinition.TableName;
                try
                {
                    DatabaseData =
                        sqliteService.GetData(
                            tableDefinition.TableName);

                    if (!string.IsNullOrWhiteSpace(
                            tableDefinition.PrimaryKey))
                    {
                        DatabaseData.DefaultView.Sort =
                            $"[{tableDefinition.PrimaryKey}] ASC";
                    }

                    OnPropertyChanged(nameof(DatabaseData));
                    OnPropertyChanged(nameof(DatabaseDataView));

                    DatabaseFields.Clear();

                    foreach (DataColumn column in DatabaseData.Columns)
                    {
                        DatabaseFields.Add(new DatabaseField
                        {
                            DatabaseName = connection.Name,
                            TableName = tableDefinition.TableName,
                            ColumnName = column.ColumnName
                        });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Internal SQLite data error", ex);
                }
            }

            return;
        }

        if (selectedType == DatabaseType.SampleDatabase)
        {
            var sqliteService = new InternalSqliteDatabaseService("SampleDatabase.db");

            sqliteService.InitializeSampleDatabase();

            var connection =
                new DatabaseConnection
                {
                    Name = "Sample Database",
                    Type = DatabaseType.SampleDatabase,
                    ConnectionString = sqliteService.ConnectionString,
                    IsConnected = true
                };

            var designDialog =
                new DatabaseDesignDialogViewModel(
                    connection,
                    _dialogService);

            var tableDefinition =
                await _dialogService.ShowDialogAsync<
                    DatabaseDesignDialogViewModel,
                    DatabaseTableDefinition?>(designDialog);

            if (tableDefinition is not null)
            {
                DatabaseTables.Add(tableDefinition);

                _activeDatabaseTableName =
                    tableDefinition.TableName;

                try
                {
                    DatabaseData =
                        sqliteService.GetData(
                            tableDefinition.TableName);

                    if (!string.IsNullOrWhiteSpace(
                            tableDefinition.PrimaryKey))
                    {
                        DatabaseData.DefaultView.Sort =
                            $"[{tableDefinition.PrimaryKey}] ASC";
                    }

                    OnPropertyChanged(
                        nameof(DatabaseData));

                    OnPropertyChanged(
                        nameof(DatabaseDataView));

                    DatabaseFields.Clear();

                    foreach (var column in tableDefinition.Columns)
                    {
                        if (!DatabaseData.Columns.Contains(
                                column.Name))
                        {
                            continue;
                        }

                        DatabaseFields.Add(
                            new DatabaseField
                            {
                                DatabaseName =
                                    connection.Name,

                                TableName =
                                    tableDefinition.TableName,

                                ColumnName =
                                    column.Name
                            });
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Sample database data error", ex);
                }
            }

            return;
        }

        if (selectedType == DatabaseType.API)
        {
            var apiDialog = new ApiConnectionDialogViewModel();

            var connection = await _dialogService.ShowDialogAsync<ApiConnectionDialogViewModel, DatabaseConnection?>(apiDialog);

            if (connection is null)
                return;

            try
            {
                var apiService = new ApiDatabaseService();

                // Use the same authentication the user tested with
                var json = await apiService.GetAsync(
                    connection.ConnectionString,
                    string.IsNullOrWhiteSpace(connection.AuthenticationType)
                        ? "None"
                        : connection.AuthenticationType,
                    connection.ApiKey ?? string.Empty,
                    connection.BearerToken ?? string.Empty,
                    connection.Username,
                    connection.Password);

                var data = apiService.ConvertJsonToDataTable(json);

                if (data.Columns.Count == 0)
                {
                    Log.Debug("API returned no columns.");
                    return;
                }

                // Give the API source a real table name instead of "API"
                var tableName = GetApiTableName(connection.ConnectionString);

                var primaryKey = data.Columns
                    .Cast<DataColumn>()
                    .FirstOrDefault(c =>
                        c.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase));

                var tableDefinition = new DatabaseTableDefinition
                {
                    DatabaseName = connection.Name,
                    TableName = tableName,
                    ConnectionString = connection.ConnectionString,
                    PrimaryKey = primaryKey?.ColumnName ?? string.Empty,
                    Columns = data.Columns
                        .Cast<DataColumn>()
                        .Select(c => new DatabaseColumn
                        {
                            Name = c.ColumnName,
                            DataType = c.DataType.Name,
                            IsPrimaryKey = primaryKey?.ColumnName == c.ColumnName,
                            IsSelected = true
                        })
                        .ToList()
                };

                DatabaseTables.Add(tableDefinition);

                // Set this BEFORE raising DatabaseData changes so the designer
                // resolves fields against the correct table
                _activeDatabaseTableName = tableName;

                if (!string.IsNullOrWhiteSpace(tableDefinition.PrimaryKey))
                {
                    data.DefaultView.Sort = $"[{tableDefinition.PrimaryKey}] ASC";
                }

                DatabaseData = data;

                OnPropertyChanged(nameof(DatabaseData));
                OnPropertyChanged(nameof(DatabaseDataView));

                DatabaseFields.Clear();

                foreach (DataColumn column in DatabaseData.Columns)
                {
                    DatabaseFields.Add(new DatabaseField
                    {
                        DatabaseName = connection.Name,
                        TableName = tableName,
                        ColumnName = column.ColumnName
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error("API data error", ex);
            }

            return;
        }

        if (selectedType == DatabaseType.GoogleSheets)
        {
            var sheetsDialog = new GoogleSheetsConnectionDialogViewModel();

            var connection =
                await _dialogService.ShowDialogAsync<GoogleSheetsConnectionDialogViewModel, DatabaseConnection?>(sheetsDialog);

            if (connection is null)
                return;

            try
            {
                var sheetsService = new GoogleSheetsDatabaseService();

                // Public links load directly; signed-in sheets reuse the dialog's sign-in.
                var data = await sheetsService.LoadAsync(connection, CancellationToken.None);

                if (data.Columns.Count == 0)
                {
                    Log.Debug("GOOGLE SHEETS: sheet has no columns.");
                    return;
                }

                var tableName = string.IsNullOrWhiteSpace(connection.SelectedTable)
                    ? "Sheet1"
                    : connection.SelectedTable;

                LoadExternalTable(connection, tableName, data);
            }
            catch (Exception ex)
            {
                Log.Error("Google Sheets data error", ex);
            }

            return;
        }


    }

    private void LoadExternalTable(
     DatabaseConnection connection,
     string tableName,
     DataTable data)
    {
        var primaryKey = data.Columns
            .Cast<DataColumn>()
            .FirstOrDefault(c =>
                c.ColumnName.Equals("ID", StringComparison.OrdinalIgnoreCase));

        var tableDefinition = new DatabaseTableDefinition
        {
            DatabaseName = connection.Name,
            TableName = tableName,
            ConnectionString = connection.ConnectionString,
            PrimaryKey = primaryKey?.ColumnName ?? string.Empty,
            Columns = data.Columns
                .Cast<DataColumn>()
                .Select(c => new DatabaseColumn
                {
                    Name = c.ColumnName,
                    DataType = c.DataType.Name,
                    IsPrimaryKey = primaryKey?.ColumnName == c.ColumnName,
                    IsSelected = true
                })
                .ToList()
        };

        DatabaseTables.Add(tableDefinition);

        _activeDatabaseTableName = tableName;

        if (!string.IsNullOrWhiteSpace(tableDefinition.PrimaryKey))
        {
            data.DefaultView.Sort = $"[{tableDefinition.PrimaryKey}] ASC";
        }

        DatabaseData = data;

        OnPropertyChanged(nameof(DatabaseData));
        OnPropertyChanged(nameof(DatabaseDataView));

        DatabaseFields.Clear();

        foreach (DataColumn column in DatabaseData.Columns)
        {
            DatabaseFields.Add(new DatabaseField
            {
                DatabaseName = connection.Name,
                TableName = tableName,
                ColumnName = column.ColumnName
            });
        }
    }

    /// <summary>Clears all database state and returns the UI to "Add Database" only.
    /// Element bindings on the card (DatabaseColumn etc.) are design data and are kept.</summary>
    [RelayCommand]
    private async Task DisconnectDatabase()
    {
        // Rows typed into the Internal Database grid are saved first; if some can't
        // be, ask before throwing them away.
        if (!await ConfirmLeaveDatabaseAsync("Disconnect"))
            return;

        // Clear the row selection first, while the old DataView is still bound.
        // OnSelectedDatabaseRowChanged clears SelectedDatabaseValues as well.
        SelectedDatabaseRow = null;

        DatabaseData = null;
        DatabaseTables.Clear();
        DatabaseFields.Clear();
        DatabaseColumns.Clear();
        SelectedDatabaseValues.Clear();

        _activeDatabaseTableName = string.Empty;
        _activeDatabaseType = null;

        OnPropertyChanged(nameof(DatabaseData));
        OnPropertyChanged(nameof(DatabaseDataView));

        // Redraw so previewed database values disappear from the canvas.
        NotifyDocumentChanged();
    }


    // ------------------------------------------------ database grid toolbar ----
    private static readonly string[] ImageColumnKeywords =
        { "photo", "image", "picture", "avatar", "logo", "signature" };

    /// <summary>Columns shown as a clickable thumbnail in the grid instead of text.</summary>
    public bool IsDatabaseImageColumn(DataColumn column) =>
        column.DataType == typeof(byte[]) ||
        ImageColumnKeywords.Any(k => column.ColumnName.Contains(k, StringComparison.OrdinalIgnoreCase));

    private readonly Dictionary<string, Avalonia.Media.Imaging.Bitmap?> _databaseImageCache = new();

    /// <summary>Loads a small thumbnail for a grid cell. Accepts image bytes (BLOB),
    /// an InfoID asset reference, or a plain file path. Returns null if the value
    /// isn't a loadable image.</summary>
    public Avalonia.Media.Imaging.Bitmap? GetDatabaseImage(object? value)
    {
        try
        {
            if (value is byte[] bytes && bytes.Length > 0)
            {
                using var stream = new MemoryStream(bytes);
                return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 96);
            }

            if (value is not string text || string.IsNullOrWhiteSpace(text))
                return null;

            if (_databaseImageCache.TryGetValue(text, out var cached))
                return cached;

            var path = _assetService.ResolveToFullPath(text);
            if (path is null || !File.Exists(path))
                path = File.Exists(text) ? text : null;

            Avalonia.Media.Imaging.Bitmap? bitmap = null;
            if (path is not null)
            {
                using var stream = File.OpenRead(path);
                bitmap = Avalonia.Media.Imaging.Bitmap.DecodeToWidth(stream, 96);
            }

            _databaseImageCache[text] = bitmap;
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Error("Database image load error", ex);
            return null;
        }
    }

    /// <summary>Grid Photo cell click: pick an image file and put it in that row's
    /// column. Text columns get an InfoID asset reference (the file is copied into
    /// the assets folder, same as images placed on the card); BLOB columns get the
    /// image bytes.</summary>
    public async Task PickDatabaseImageAsync(DataRowView rowView, string columnName)
    {
        if (DatabaseData is null) return;

        var row = rowView.Row;
        if (row.Table != DatabaseData ||
            row.RowState == DataRowState.Detached ||
            !DatabaseData.Columns.Contains(columnName))
            return;

        var path = await _filePicker.PickImageFileAsync("Choose Image");
        if (path is null) return;

        var column = DatabaseData.Columns[columnName]!;

        try
        {
            column.ReadOnly = false;

            if (column.DataType == typeof(byte[]))
                row[column] = await File.ReadAllBytesAsync(path);
            else
                row[column] = await _assetService.ImportAsync(path);
        }
        catch (Exception ex)
        {
            Log.Error("Database image pick error", ex);
            return;
        }

        // Rebuild the grid so the new thumbnail shows, keeping this row selected.
        var rowIndex = DatabaseData.Rows.IndexOf(row);

        ReplaceDatabaseTable(DatabaseData, DatabaseData.DefaultView.RowFilter);

        if (rowIndex >= 0 && rowIndex < DatabaseData.Rows.Count)
        {
            var updatedRow = DatabaseData.Rows[rowIndex];
            SelectedDatabaseRow = DatabaseData.DefaultView
                .Cast<DataRowView>()
                .FirstOrDefault(v => v.Row == updatedRow);
        }

        NotifyDocumentChanged();
    }


    // ------------------------------------------------------------ photo folder ----

    private readonly HashSet<string> _preparedPhotoFolders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Result of the last "Check photos", shown in the Image Parameters tab.</summary>
    [ObservableProperty]
    private string _photoCheckSummary = string.Empty;

    private static Dictionary<string, string> RowToRecord(DataRow row)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (DataColumn column in row.Table.Columns)
        {
            var value = row[column];
            values[column.ColumnName] = value == DBNull.Value ? string.Empty : value?.ToString() ?? string.Empty;
        }

        return values;
    }

    /// <summary>What the canvas should draw for an Image element. With a grid row
    /// selected and either "Use photos from a folder" on or the image bound to a
    /// database image column, that's the selected record's photo; otherwise the
    /// element's own fixed image, exactly as before.</summary>
    public (Avalonia.Media.Imaging.Bitmap? Bitmap, string? AssetReference, bool ShowPlaceholder)
        ResolveImageSource(ImageElement image, double drawWidthPx)
    {
        if (SelectedDatabaseRow is not { } rowView ||
            rowView.Row.RowState == DataRowState.Detached)
        {
            return (null, image.AssetReference, true);
        }

        string? path;
        var service = PhotoFolderService.Shared;

        if (image.UsePhotoFolder && !string.IsNullOrWhiteSpace(image.PhotoFolderPath))
        {
            var folder = image.PhotoFolderPath;
            EnsurePhotoFolderPrepared(folder);

            var key = service.BuildKey(image.PhotoFileNamePattern, RowToRecord(rowView.Row));
            path = key is null ? null : service.FindPhoto(folder, key);
        }
        else if (image.IsDatabaseBound &&
                 !string.IsNullOrWhiteSpace(image.DatabaseColumn) &&
                 rowView.Row.Table.Columns.Contains(image.DatabaseColumn))
        {
            // Image bound to a database column: the selected row's own photo, same
            // resolution the Print dialog uses, so canvas and printed card match.
            path = ResolveDatabaseImageValue(rowView.Row[image.DatabaseColumn]);
        }
        else
        {
            return (null, image.AssetReference, true);
        }

        // Rounded up to 64px steps so zooming doesn't reload the photo at every tiny size change.
        var decodeWidth = Math.Max(64, ((int)Math.Ceiling(drawWidthPx) + 63) / 64 * 64);
        var bitmap = path is null ? null : service.LoadPhoto(path, decodeWidth);

        if (bitmap is not null)
            return (bitmap, null, false);

        return image.MissingPhotoBehavior switch
        {
            MissingPhotoBehavior.Blank => (null, null, false),
            MissingPhotoBehavior.DefaultImage => (null, image.AssetReference, true),
            _ => (null, null, true),
        };
    }

    /// <summary>Scans a folder in the background the first time it's needed, then
    /// redraws so photos appear as soon as the scan finishes.</summary>
    private void EnsurePhotoFolderPrepared(string folderPath)
    {
        if (!_preparedPhotoFolders.Add(folderPath))
            return;

        _ = PreparePhotoFolderAsync(folderPath);
    }

    private async Task PreparePhotoFolderAsync(string folderPath)
    {
        try
        {
            await PhotoFolderService.Shared.PrepareFolderAsync(folderPath);
            NotifyDocumentChanged();
        }
        catch (Exception ex)
        {
            Log.Warn("Photo folder prepare error", ex);
            _preparedPhotoFolders.Remove(folderPath);
        }
    }

    /// <summary>Parameters tab "Browse...": choose the photos folder, pick a sensible
    /// file name pattern from the connected data, and check which records match.</summary>
    [RelayCommand]
    private async Task BrowsePhotoFolder(ImageElement? image)
    {
        if (image is null) return;

        var folder = await _filePicker.PickFolderAsync("Choose Photo Folder");
        if (folder is null) return;

        image.PhotoFolderPath = folder;
        image.UsePhotoFolder = true;

        // Default pattern: use the data's ID column if it isn't literally "EmployeeID".
        if (DatabaseData is not null && image.PhotoFileNamePattern == "{EmployeeID}")
        {
            var columns = DatabaseData.Columns.Cast<DataColumn>().Select(c => c.ColumnName);
            if (PhotoFolderService.Shared.SuggestKeyColumn(columns) is { } keyColumn)
                image.PhotoFileNamePattern = "{" + keyColumn + "}";
        }

        await RefreshPhotoFolder(image);
    }

    /// <summary>Parameters tab "Refresh": rescan the folder (new or renamed photos)
    /// and re-run the check.</summary>
    [RelayCommand]
    private async Task RefreshPhotoFolder(ImageElement? image)
    {
        if (image?.PhotoFolderPath is not { Length: > 0 } folder) return;

        _preparedPhotoFolders.Remove(folder);
        await PhotoFolderService.Shared.RefreshFolderAsync(folder);
        _preparedPhotoFolders.Add(folder);

        await CheckPhotoFolder(image);
        NotifyDocumentChanged();
    }

    /// <summary>Parameters tab "Check photos": how many records have a matching photo,
    /// and which IDs are missing, before printing.</summary>
    [RelayCommand]
    private async Task CheckPhotoFolder(ImageElement? image)
    {
        if (image is null || string.IsNullOrWhiteSpace(image.PhotoFolderPath))
        {
            PhotoCheckSummary = "Choose a photo folder first.";
            return;
        }

        if (!Directory.Exists(image.PhotoFolderPath))
        {
            PhotoCheckSummary = "Folder not found on this PC. Click Browse to choose it.";
            return;
        }

        if (DatabaseData is null)
        {
            PhotoCheckSummary = "Connect a database to check which records have a photo.";
            return;
        }

        await PhotoFolderService.Shared.PrepareFolderAsync(image.PhotoFolderPath);

        var records = DatabaseData.Rows.Cast<DataRow>().Select(RowToRecord);
        var report = PhotoFolderService.Shared.CheckRecords(
            image.PhotoFolderPath, image.PhotoFileNamePattern, records);

        if (report.MissingRecords == 0)
        {
            PhotoCheckSummary = $"All {report.TotalRecords} records have a photo.";
            return;
        }

        var shown = string.Join(", ", report.MissingKeys.Take(10));
        var more = report.MissingKeys.Count > 10 ? $" and {report.MissingKeys.Count - 10} more" : string.Empty;

        PhotoCheckSummary =
            $"{report.MatchedRecords} of {report.TotalRecords} records have a photo. Missing: {shown}{more}";
    }


    private const string AllColumnsOption = "All";

    /// <summary>"All" plus every column name, for the search column dropdown.</summary>
    public ObservableCollection<string> SearchColumnOptions { get; } = new() { AllColumnsOption };

    [ObservableProperty]
    private string _databaseSearchText = string.Empty;

    [ObservableProperty]
    private string? _selectedSearchColumn = AllColumnsOption;

    /// <summary>"5 of 12" -- position of the current row among the visible rows.</summary>
    public string RecordPositionText
    {
        get
        {
            var rows = GetVisibleDatabaseRows();
            if (rows.Count == 0) return "0 of 0";
            var index = GetCurrentRowIndex(rows);
            return index < 0 ? $"- of {rows.Count}" : $"{index + 1} of {rows.Count}";
        }
    }

    private List<DataRowView> GetVisibleDatabaseRows() =>
        DatabaseDataView?.Cast<DataRowView>().ToList() ?? new List<DataRowView>();

    private int GetCurrentRowIndex(List<DataRowView> rows) =>
        SelectedDatabaseRow is null ? -1 : rows.FindIndex(r => r.Row == SelectedDatabaseRow.Row);

    private void MoveToDatabaseRow(Func<int, int, int> pickIndex)
    {
        var rows = GetVisibleDatabaseRows();
        if (rows.Count == 0) return;

        var target = Math.Clamp(pickIndex(GetCurrentRowIndex(rows), rows.Count), 0, rows.Count - 1);
        SelectedDatabaseRow = rows[target];
    }

    [RelayCommand]
    private void FirstDatabaseRow() => MoveToDatabaseRow((_, _) => 0);

    [RelayCommand]
    private void PreviousDatabaseRow() => MoveToDatabaseRow((i, _) => i < 0 ? 0 : i - 1);

    [RelayCommand]
    private void NextDatabaseRow() => MoveToDatabaseRow((i, _) => i + 1);

    [RelayCommand]
    private void LastDatabaseRow() => MoveToDatabaseRow((_, count) => count - 1);

    [RelayCommand]
    private void ApplyDatabaseSearch()
    {
        if (DatabaseData is null) return;

        SelectedDatabaseRow = null;

        try
        {
            ReplaceDatabaseTable(DatabaseData, BuildDatabaseRowFilter());
        }
        catch (Exception ex)
        {
            Log.Error("Database search error", ex);
            ReplaceDatabaseTable(DatabaseData, string.Empty);
        }

        SelectedDatabaseRow = GetVisibleDatabaseRows().FirstOrDefault();
        NotifyDocumentChanged();
    }

    [RelayCommand]
    private void ClearDatabaseSearch()
    {
        DatabaseSearchText = string.Empty;
        SelectedSearchColumn = AllColumnsOption;

        if (DatabaseData is null) return;

        SelectedDatabaseRow = null;
        ReplaceDatabaseTable(DatabaseData, string.Empty);
        NotifyDocumentChanged();
    }

    /// <summary>The grid only refreshes when it receives a new DataView, so filtering
    /// and deleting swap in a copy of the table (same pattern AddDatabaseRow uses),
    /// keeping the existing sort.</summary>
    private void ReplaceDatabaseTable(DataTable source, string rowFilter)
    {
        var sort = source.DefaultView.Sort;
        var copy = source.Copy();

        // DataTable.Copy keeps row order, so ticks map across by index.
        var remapped = new List<DataRow>();
        for (var i = 0; i < source.Rows.Count; i++)
        {
            if (_checkedDatabaseRows.Contains(source.Rows[i]))
                remapped.Add(copy.Rows[i]);
        }

        _checkedDatabaseRows.Clear();
        foreach (var row in remapped)
            _checkedDatabaseRows.Add(row);

        copy.DefaultView.Sort = sort;
        copy.DefaultView.RowFilter = rowFilter;

        DatabaseData = copy;

        OnPropertyChanged(nameof(DatabaseData));
        OnPropertyChanged(nameof(DatabaseDataView));
    }

    private string BuildDatabaseRowFilter()
    {
        if (DatabaseData is null || string.IsNullOrWhiteSpace(DatabaseSearchText))
            return string.Empty;

        var pattern = EscapeLikeValue(DatabaseSearchText.Trim());

        var columns = string.IsNullOrEmpty(SelectedSearchColumn) || SelectedSearchColumn == AllColumnsOption
            ? DatabaseData.Columns.Cast<DataColumn>().Select(c => c.ColumnName)
            : new[] { SelectedSearchColumn };

        return string.Join(" OR ", columns.Select(c =>
            $"CONVERT([{c.Replace("\\", "\\\\").Replace("]", "\\]")}], 'System.String') LIKE '%{pattern}%'"));
    }

    // Makes typed text literal inside a DataView LIKE pattern.
    private static string EscapeLikeValue(string value)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '[': sb.Append("[[]"); break;
                case ']': sb.Append("[]]"); break;
                case '*': sb.Append("[*]"); break;
                case '%': sb.Append("[%]"); break;
                case '\'': sb.Append("''"); break;
                default: sb.Append(ch); break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Rebuilds the column dropdown only when the columns actually change
    /// (new connection or disconnect), so a chosen column survives Apply.</summary>
    private void RefreshSearchColumns()
    {
        if (DatabaseData is null)
            DatabaseSearchText = string.Empty;

        var names = DatabaseData?.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList()
                    ?? new List<string>();

        if (SearchColumnOptions.Skip(1).SequenceEqual(names))
            return;

        SearchColumnOptions.Clear();
        SearchColumnOptions.Add(AllColumnsOption);
        foreach (var name in names)
            SearchColumnOptions.Add(name);

        SelectedSearchColumn = AllColumnsOption;
    }

    private static string GetApiTableName(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var lastSegment = uri.Segments
                .LastOrDefault()?
                .Trim('/');

            if (!string.IsNullOrWhiteSpace(lastSegment))
                return lastSegment;
        }

        return "API";
    }
    /// <summary>
    /// Checks if a DataRow contains any actual data (non-null, non-empty values)
    /// in columns other than auto-increment primary keys.
    /// </summary>
    private bool RowHasData(DataRow row)
    {
        foreach (DataColumn column in row.Table.Columns)
        {
            // Skip auto-increment columns (they're generated by the database).
            if (column.AutoIncrement)
                continue;

            var value = row[column];

            // If the value is not DBNull and not an empty string, the row has data.
            if (value != DBNull.Value &&
                !(value is string str && string.IsNullOrWhiteSpace(str)))
            {
                return true;
            }
        }

        return false;
    }

    [RelayCommand]
    private void InsertDatabaseField(DatabaseField? field)
    {
        if (field is null)
            return;

        var textElement = SelectedElements
            .OfType<TextElement>()
            .FirstOrDefault();

        if (textElement is null)
            return;

        var binding = $"{{{{{field.ColumnName}}}}}";

        if (string.IsNullOrWhiteSpace(textElement.DataBindingExpression))
        {
            textElement.DataBindingExpression = binding;
        }
        else
        {
            textElement.DataBindingExpression += " " + binding;
        }
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
        var commands = SelectedElements
            .Select(e => (IDesignCommand)new ChangePropertyCommand<DesignerElement, string?>(
                e, (el, v) => el.GroupId = v, e.GroupId, groupId, "Group"))
            .ToList();

        History.Execute(new CompositeCommand("Group elements", commands));
        RefreshHistoryFlags();
        NotifyDocumentChanged();
        MarkDirty();
    }

    [RelayCommand]
    private void Ungroup()
    {
        var grouped = SelectedElements.Where(e => e.GroupId is not null).ToList();
        if (grouped.Count == 0) return;

        var commands = grouped
            .Select(e => (IDesignCommand)new ChangePropertyCommand<DesignerElement, string?>(
                e, (el, v) => el.GroupId = v, e.GroupId, null, "Ungroup"))
            .ToList();

        History.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand("Ungroup elements", commands));
        RefreshHistoryFlags();
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

        // Data-grid rows (Internal Database) are saved with the design. Autosave leaves
        // the row being typed in alone; it's saved when that row is finished.
        SaveDatabaseRows(includeCurrentRow: !isAutosave);

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
                    Log.Warn("Thumbnail generation failed", thumbnailEx);
                }
            }
        }
        catch (Exception ex)
        {
            SaveStatusText = isAutosave ? "Autosave failed" : "Save failed";
            Log.Error("Card design save failed", ex);
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
