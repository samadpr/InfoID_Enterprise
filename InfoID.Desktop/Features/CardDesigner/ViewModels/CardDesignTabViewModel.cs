using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.CardDesigner.History;
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
    private CancellationTokenSource? _autosaveCts;

    public CardDesignDocument Document { get; }
    public DesignHistory History { get; } = new();

    /// <summary>Raised when something outside a live canvas drag changes the document
    /// (undo/redo, insert/delete/paste) so the canvas knows to redraw.</summary>
    public event EventHandler? DocumentChanged;

    public CardDesignTabViewModel(
        CardDesignDocument document, IDesignClipboard clipboard, ICardDesignRepository repository,
        IFilePickerService filePicker, IDesignAssetService assetService,
        IDataBindingEvaluator evaluator, IPreviewDataProvider previewDataProvider)
    {
        Document = document;
        _clipboard = clipboard;
        _repository = repository;
        _filePicker = filePicker;
        _assetService = assetService;
        _evaluator = evaluator;
        _previewDataProvider = previewDataProvider;
        PreviewRecords = new ObservableCollection<PreviewRecord>(previewDataProvider.GetSampleRecords());
        SelectedPreviewRecord = PreviewRecords.FirstOrDefault();
        Title = document.Name;

        HookSide(Document.Front);
        HookSide(Document.Back);
    }

    // ------------------------------------------------------ live element notifications ----

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

    public CardDesignSide FocusedSideModel => Document.GetSide(FocusedSide);

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

    [RelayCommand]
    private void AddRectangleElement() => InsertElement(new ShapeElement { Name = "Rectangle", Kind = ShapeKind.Rectangle, X = 10, Y = 10, Width = 30, Height = 20 });

    [RelayCommand]
    private void AddEllipseElement() => InsertElement(new ShapeElement { Name = "Ellipse", Kind = ShapeKind.Ellipse, X = 10, Y = 10, Width = 20, Height = 20 });

    [RelayCommand]
    private void AddLineElement() => InsertElement(new ShapeElement { Name = "Line", Kind = ShapeKind.Line, X = 10, Y = 10, Width = 30, Height = 0, StrokeWidth = 1, FillEnabled = false });

    [RelayCommand]
    private void AddArrowElement() => InsertElement(new ShapeElement { Name = "Arrow", Kind = ShapeKind.Arrow, X = 10, Y = 10, Width = 30, Height = 0, StrokeWidth = 1.2, FillEnabled = false });

    [RelayCommand]
    private void AddTriangleElement() => InsertElement(new ShapeElement { Name = "Triangle", Kind = ShapeKind.Triangle, X = 10, Y = 10, Width = 24, Height = 20 });

    [RelayCommand]
    private void AddPolygonElement() => InsertElement(new ShapeElement { Name = "Polygon", Kind = ShapeKind.Polygon, PolygonSides = 6, X = 10, Y = 10, Width = 22, Height = 22 });

    [RelayCommand]
    private void AddStarElement() => InsertElement(new ShapeElement { Name = "Star", Kind = ShapeKind.Star, X = 10, Y = 10, Width = 22, Height = 22 });

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
    private void SelectTool() => PanToolActive = false;

    [RelayCommand]
    private void PanTool() => PanToolActive = true;

    [RelayCommand]
    private void SetSideFront() => ActiveSideView = DesignerSideView.Front;

    [RelayCommand]
    private void SetSideBack() => ActiveSideView = DesignerSideView.Back;

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
    private async Task Save()
    {
        _autosaveCts?.Cancel();
        SaveStatusText = "Saving...";
        try
        {
            await _repository.SaveAsync(Document);
            IsDirty = false;
            SaveStatusText = "Saved";
        }
        catch (Exception ex)
        {
            SaveStatusText = "Save failed";
            System.Diagnostics.Debug.WriteLine($"Card design save failed: {ex}");
        }
    }

    [RelayCommand]
    private async Task SaveVersion()
    {
        if (PersistedTemplateId(Document) is not { } templateId) { await Save(); templateId = PersistedTemplateId(Document)!.Value; }
        Document.VersionNumber++;
        await _repository.SaveVersionSnapshotAsync(templateId, Document, changeNote: null);
        await Save();
    }

    private static long? PersistedTemplateId(CardDesignDocument document) => document.PersistedTemplateId;

    /// <summary>Debounced autosave (Part 32) -- restarts a 2-second timer on every
    /// meaningful change instead of saving on every keystroke/drag frame. Runs on a
    /// background delay via Task.Delay, then marshals the actual save back onto the UI
    /// thread since it touches ObservableProperty state.</summary>
    private void ScheduleAutosave()
    {
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

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            SaveStatusText = "Autosaving...";
            try
            {
                await _repository.SaveAsync(Document);
                IsDirty = false;
                SaveStatusText = "Autosaved";
            }
            catch (Exception ex)
            {
                SaveStatusText = "Autosave failed";
                System.Diagnostics.Debug.WriteLine($"Autosave failed: {ex}");
            }
        });
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