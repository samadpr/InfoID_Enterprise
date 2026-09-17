using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.BlankCard.Models;
using InfoID.Desktop.Features.CardDesigner.Models;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.Printing.Models;
using InfoID.Desktop.Features.Printing.Services;

namespace InfoID.Desktop.Features.Printing.ViewModels;

/// <summary>
/// The Print module (the user's own dedicated area of this codebase) -- printer
/// selection, card format/orientation, front/back + copies + bulk-record range, a real
/// rendered preview (color and a real monochrome simulation), rendering options, and
/// per-record print-status tracking across app restarts.
///
/// Deliberately separate from PrintPreviewViewModel (which stays exactly as it was:
/// appearance-only, no printer/copies/print action -- see its own doc comment) --
/// this is the "Part 42" printing engine that comment said didn't exist yet.
///
/// Bulk printing operates against IPreviewDataProvider's sample records (the same ones
/// Data Preview and Print Preview already use) since the real Cardholder Management /
/// database module is a separate, not-yet-built part of this project; swapping to real
/// records later only needs a different IPreviewDataProvider, exactly like Data Preview.
/// </summary>
public sealed partial class PrintDialogViewModel : DialogViewModelBase<bool>
{
    private readonly IPrinterService _printerService;
    private readonly IPrintStatusStore _statusStore;
    private readonly ICardFormatCatalogService _formatCatalogService;
    private readonly IDataBindingEvaluator _evaluator;
    private readonly IDesignAssetService _assetService;
    private readonly IFilePickerService _filePickerService;
    private static readonly IReadOnlyDictionary<string, string> EmptyFields = new Dictionary<string, string>();

    public override string Title => "Print";
    public override bool CanResize => true;
    public override (double Width, double Height)? PreferredSize => (980, 700);

    public CardDesignDocument Document { get; }

    // ---------------------------------------------------------------- destination ----

    public ObservableCollection<PrinterInfo> Printers { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPrinterOnline))]
    [NotifyPropertyChangedFor(nameof(DriverName))]
    [NotifyPropertyChangedFor(nameof(DriverStatus))]
    [NotifyPropertyChangedFor(nameof(DriverIsDefault))]
    private PrinterInfo? _selectedPrinter;

    public bool IsPrinterOnline => SelectedPrinter?.IsOnline ?? false;
    public string DriverName => SelectedPrinter?.Name ?? "No printer selected";
    public string DriverStatus => SelectedPrinter is null ? "-" : SelectedPrinter.IsOnline ? "Online" : "Offline";
    public string DriverIsDefault => SelectedPrinter is { IsDefault: true } ? "Yes" : "No";

    [ObservableProperty]
    private bool _printingSupportedHere;

    // ---------------------------------------------------------------- card format ----

    public ObservableCollection<CardFormatDefinition> CardFormats { get; } = new();

    [ObservableProperty]
    private CardFormatDefinition? _selectedCardFormat;

    public ObservableCollection<string> OrientationOptions { get; } = new() { "Landscape", "Portrait" };

    [ObservableProperty]
    private string _selectedOrientationOption = "Landscape";

    // ---------------------------------------------------------------- print operations ----

    [ObservableProperty]
    private bool _printFront = true;

    [ObservableProperty]
    private bool _printBack = true;

    // ---------------------------------------------------------------- print range/copies ----

    [ObservableProperty]
    private int _numberOfCards = 1;

    [ObservableProperty]
    private int _numberOfCopies = 1;

    [ObservableProperty]
    private bool _reversedOrder;

    // ---------------------------------------------------------------- rendering options ----

    public ObservableCollection<PrintColorMode> ColorModeOptions { get; } = new()
    {
        PrintColorMode.MonoChrome, PrintColorMode.Composite, PrintColorMode.CompositeAndMonoChrome,
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMonoChrome))]
    private PrintColorMode _colorMode = PrintColorMode.MonoChrome;

    public bool IsMonoChrome => ColorMode != PrintColorMode.Composite;

    public ObservableCollection<PrintAntialiasMode> AntialiasModeOptions { get; } = new()
    {
        PrintAntialiasMode.Yes, PrintAntialiasMode.OnlyText, PrintAntialiasMode.OnlyImages, PrintAntialiasMode.No,
    };

    [ObservableProperty]
    private PrintAntialiasMode _antialiasMode = PrintAntialiasMode.OnlyImages;

    [ObservableProperty]
    private double _blackPanelThreshold = 128;

    [ObservableProperty]
    private bool _rotate180;

    [ObservableProperty]
    private bool _rotate180Landscape;

    [ObservableProperty]
    private bool _rotate180Portrait;

    public ObservableCollection<DigitalCopyMode> DigitalCopyModeOptions { get; } = new()
    {
        DigitalCopyMode.Off, DigitalCopyMode.PdfFile, DigitalCopyMode.ImageFiles,
    };

    [ObservableProperty]
    private DigitalCopyMode _digitalCopyMode = DigitalCopyMode.Off;

    // ---------------------------------------------------------------- preview ----

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowFrontColorPanel))]
    [NotifyPropertyChangedFor(nameof(ShowFrontBlackPanel))]
    private bool _previewShowFront = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowBackColorPanel))]
    [NotifyPropertyChangedFor(nameof(ShowBackBlackPanel))]
    private bool _previewShowBack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewShowBlack))]
    [NotifyPropertyChangedFor(nameof(ShowFrontColorPanel))]
    [NotifyPropertyChangedFor(nameof(ShowFrontBlackPanel))]
    [NotifyPropertyChangedFor(nameof(ShowBackColorPanel))]
    [NotifyPropertyChangedFor(nameof(ShowBackBlackPanel))]
    private bool _previewShowColor = true;

    public bool PreviewShowBlack => !PreviewShowColor;

    // Bug fix: the Front/Back filter checkboxes previously had nothing bound to them --
    // both preview panels always showed regardless, because the panels' IsVisible only
    // checked which tab (Color/Black) was active. Each panel now needs BOTH conditions
    // (this side is turned on AND this tab is active), so these four combine them into
    // single bindable bools instead of needing a MultiBinding in the View.
    public bool ShowFrontColorPanel => PreviewShowFront && PreviewShowColor;
    public bool ShowBackColorPanel => PreviewShowBack && PreviewShowColor;
    public bool ShowFrontBlackPanel => PreviewShowFront && PreviewShowBlack;
    public bool ShowBackBlackPanel => PreviewShowBack && PreviewShowBlack;

    [ObservableProperty]
    private PreviewRecord? _previewRecord;

    /// <summary>Real rasterized-and-thresholded bitmaps for the Preview tab's "Black"
    /// sub-tab -- a genuine simulation of what a MonoChrome print will look like (same
    /// ApplyMonoChrome pixel transform Print itself uses), not a color preview with a
    /// grayscale CSS-style filter standing in for it. Recomputed on demand rather than
    /// on every threshold-slider tick, since rasterizing at print DPI isn't free.</summary>
    [ObservableProperty]
    private Bitmap? _blackPreviewFront;

    [ObservableProperty]
    private Bitmap? _blackPreviewBack;

    // ---------------------------------------------------------------- advanced print operations ----

    [ObservableProperty]
    private bool _updatePrintStatusEnabled;

    public ObservableCollection<string> AvailableFieldKeys { get; } = new();

    /// <summary>Repeatable (source column -> marker) mapping rows, matching the
    /// reference's +/- row list -- each row is independent, so e.g. one record field
    /// can drive $PRINTSTATUS while another drives $PRINTCOUNTER in the same run.</summary>
    public ObservableCollection<PrintStatusMappingRow> StatusMappings { get; } = new();

    public ObservableCollection<PrintStatusMarker> StatusMarkerOptions { get; } = new()
    {
        PrintStatusMarker.PrintStatus, PrintStatusMarker.PrintCounter, PrintStatusMarker.PrintDate,
        PrintStatusMarker.PrintStatusAndCounter, PrintStatusMarker.PrintStatusAndCounterAndDate,
    };

    public ObservableCollection<PrintQueueRow> Records { get; } = new();

    // ---------------------------------------------------------------- status / progress ----

    [ObservableProperty]
    private bool _isPrinting;

    [ObservableProperty]
    private string? _statusMessage;

    public PrintDialogViewModel(
        CardDesignDocument document,
        IPrinterService printerService,
        IPrintStatusStore statusStore,
        ICardFormatCatalogService formatCatalogService,
        IPreviewDataProvider previewDataProvider,
        IDataBindingEvaluator evaluator,
        IDesignAssetService assetService,
        IFilePickerService filePickerService)
    {
        Document = document;
        _printerService = printerService;
        _statusStore = statusStore;
        _formatCatalogService = formatCatalogService;
        _evaluator = evaluator;
        _assetService = assetService;
        _filePickerService = filePickerService;

        PrintingSupportedHere = printerService.IsSupported;
        StatusMessage = PrintingSupportedHere
            ? null
            : "No supported print mechanism was found on this system -- printer selection and Print will be unavailable, but Preview and Digital Copy still work.";

        SelectedOrientationOption = document.Orientation == CardOrientation.Portrait ? "Portrait" : "Landscape";

        var records = previewDataProvider.GetSampleRecords();
        foreach (var key in records.SelectMany(r => r.Fields.Keys).Distinct())
        {
            AvailableFieldKeys.Add(key);
        }
        StatusMappings.Add(new PrintStatusMappingRow());

        NumberOfCards = Math.Max(1, records.Count);

        _ = InitializeAsync(records);
    }

    private async Task InitializeAsync(IReadOnlyList<PreviewRecord> records)
    {
        var statuses = await _statusStore.LoadAsync();
        foreach (var record in records)
        {
            statuses.TryGetValue(record.Label, out var status);
            Records.Add(new PrintQueueRow(record, status));
        }
        PreviewRecord = Records.FirstOrDefault()?.Record;
        RefreshBlackPreview();

        var formats = await _formatCatalogService.GetAllAsync();
        foreach (var format in formats) CardFormats.Add(format);
        SelectedCardFormat = CardFormats.FirstOrDefault(f => f.Id == Document.CardFormatId)
                             ?? CardFormats.FirstOrDefault(f =>
                                 Math.Abs(f.WidthMm - Document.WidthMm) < 0.05 && Math.Abs(f.HeightMm - Document.HeightMm) < 0.05)
                             ?? CardFormats.FirstOrDefault();

        await RefreshPrintersAsync();
    }

    // ---------------------------------------------------------------- render-option helpers ----

    public string? ResolveAssetPath(string? assetReference) => _assetService.ResolveToFullPath(assetReference);

    public string ResolveText(TextElement text) =>
        string.IsNullOrEmpty(text.DataBindingExpression)
            ? text.Text
            : _evaluator.Evaluate(text.DataBindingExpression, PreviewRecord?.Fields ?? EmptyFields);

    public string ResolveField(DataFieldElement field) =>
        _evaluator.Evaluate($"{{{{{field.FieldKey}}}}}", PreviewRecord?.Fields ?? EmptyFields);

    public bool IsVisibleNow(DesignerElement element) =>
        _evaluator.EvaluateCondition(element.VisibilityCondition, PreviewRecord?.Fields ?? EmptyFields);

    private ThumbnailRenderer.RenderOptions BuildRenderOptions(PreviewRecord? record) => new()
    {
        ResolveAssetPath = _assetService.ResolveToFullPath,
        ResolveText = t => string.IsNullOrEmpty(t.DataBindingExpression) ? t.Text : _evaluator.Evaluate(t.DataBindingExpression, record?.Fields ?? EmptyFields),
        ResolveField = f => _evaluator.Evaluate($"{{{{{f.FieldKey}}}}}", record?.Fields ?? EmptyFields),
        IsVisibleNow = e => _evaluator.EvaluateCondition(e.VisibilityCondition, record?.Fields ?? EmptyFields),
    };

    // ---------------------------------------------------------------- commands: destination ----

    [RelayCommand]
    private async Task RefreshPrinters() => await RefreshPrintersAsync();

    private async Task RefreshPrintersAsync()
    {
        var previouslySelected = SelectedPrinter?.Name;
        Printers.Clear();

        if (!_printerService.IsSupported)
        {
            SelectedPrinter = null;
            return;
        }

        var printers = await _printerService.GetPrintersAsync();
        foreach (var printer in printers) Printers.Add(printer);

        SelectedPrinter = Printers.FirstOrDefault(p => p.Name == previouslySelected)
                           ?? Printers.FirstOrDefault(p => p.IsDefault)
                           ?? Printers.FirstOrDefault();

        if (Printers.Count == 0)
        {
            StatusMessage = "No printers were found. Make sure a printer is installed on this system.";
        }
    }

    [RelayCommand]
    private async Task OpenPreferences()
    {
        if (SelectedPrinter is null) return;
        await _printerService.OpenPrinterPreferencesAsync(SelectedPrinter.Name);
    }

    [RelayCommand]
    private async Task AddPrinter() => await _printerService.OpenPrinterManagementAsync();

    // ---------------------------------------------------------------- commands: preview ----

    [RelayCommand]
    private void NextPreviewRecord()
    {
        if (Records.Count == 0) return;
        var index = PreviewRecord is null ? -1 : Records.ToList().FindIndex(r => r.Record == PreviewRecord);
        PreviewRecord = Records[(index + 1) % Records.Count].Record;
    }

    [RelayCommand]
    private void PreviousPreviewRecord()
    {
        if (Records.Count == 0) return;
        var index = PreviewRecord is null ? 0 : Records.ToList().FindIndex(r => r.Record == PreviewRecord);
        PreviewRecord = Records[(index - 1 + Records.Count) % Records.Count].Record;
    }

    // ---------------------------------------------------------------- commands: range/copies ----

    [RelayCommand]
    private void IncrementCards() => NumberOfCards = Math.Min(Records.Count == 0 ? 1 : Records.Count, NumberOfCards + 1);

    [RelayCommand]
    private void DecrementCards() => NumberOfCards = Math.Max(1, NumberOfCards - 1);

    [RelayCommand]
    private void IncrementCopies() => NumberOfCopies++;

    [RelayCommand]
    private void DecrementCopies() => NumberOfCopies = Math.Max(1, NumberOfCopies - 1);

    // ---------------------------------------------------------------- commands: advanced ops ----

    [RelayCommand]
    private async Task SetAllPrinted()
    {
        foreach (var row in Records)
        {
            row.IsPrinted = true;
        }
        await PersistStatusesAsync();
    }

    [RelayCommand]
    private async Task SetAllNotPrinted()
    {
        foreach (var row in Records)
        {
            row.IsPrinted = false;
        }
        await PersistStatusesAsync();
    }

    [RelayCommand]
    private async Task ToggleRecordPrinted(PrintQueueRow row)
    {
        row.IsPrinted = !row.IsPrinted;
        await PersistStatusesAsync();
    }

    [RelayCommand]
    private void AddStatusMapping() => StatusMappings.Add(new PrintStatusMappingRow());

    [RelayCommand]
    private void RemoveStatusMapping(PrintStatusMappingRow row) => StatusMappings.Remove(row);

    private Task PersistStatusesAsync() =>
        _statusStore.SaveAsync(Records.ToDictionary(r => r.Record.Label, r => r.ToStatus()));

    /// <summary>Applies every enabled mapping row's real effect to a just-printed record
    /// -- PrintStatus is already handled by the caller (row.IsPrinted = true), this only
    /// adds the two extra, opt-in effects: bumping PrintCount and/or stamping
    /// LastPrintedAtUtc, per PrintStatusMarker.</summary>
    private void ApplyStatusMappings(PrintQueueRow row)
    {
        if (!UpdatePrintStatusEnabled) return;

        foreach (var mapping in StatusMappings)
        {
            switch (mapping.Marker)
            {
                case PrintStatusMarker.PrintCounter:
                    row.PrintCount++;
                    break;
                case PrintStatusMarker.PrintDate:
                    row.LastPrintedAtUtc = DateTime.UtcNow;
                    break;
                case PrintStatusMarker.PrintStatusAndCounter:
                    row.PrintCount++;
                    break;
                case PrintStatusMarker.PrintStatusAndCounterAndDate:
                    row.PrintCount++;
                    row.LastPrintedAtUtc = DateTime.UtcNow;
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- commands: print / digital copy ----

    [RelayCommand]
    private async Task Print()
    {
        if (!_printerService.IsSupported)
        {
            StatusMessage = "Printing isn't available on this system.";
            return;
        }
        if (SelectedPrinter is null)
        {
            StatusMessage = "Select a printer first.";
            return;
        }
        if (!PrintFront && !PrintBack)
        {
            StatusMessage = "Turn on Print Front and/or Print Back first.";
            return;
        }
        if (IsPrinting) return;

        IsPrinting = true;
        StatusMessage = "Preparing pages...";
        string? tempDir = null;

        try
        {
            var recordsToPrint = Records.Take(Math.Max(1, NumberOfCards)).ToList();
            if (ReversedOrder) recordsToPrint.Reverse();

            var applyRotation = Rotate180
                || (Rotate180Landscape && Document.Orientation == CardOrientation.Landscape)
                || (Rotate180Portrait && Document.Orientation == CardOrientation.Portrait);

            tempDir = Path.Combine(Path.GetTempPath(), "InfoID_Print_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var pagePaths = new List<string>();
            var pdfPages = new List<CardPdfWriter.PdfPage>();
            var pageIndex = 0;
            foreach (var row in recordsToPrint)
            {
                var options = BuildRenderOptions(row.Record);
                if (PrintFront) foreach (var p in await SavePageAsync(tempDir, CardSide.Front, options, pageIndex++, applyRotation)) { pagePaths.Add(p.Path); pdfPages.Add(p.PdfPage); }
                if (PrintBack) foreach (var p in await SavePageAsync(tempDir, CardSide.Back, options, pageIndex++, applyRotation)) { pagePaths.Add(p.Path); pdfPages.Add(p.PdfPage); }
            }

            StatusMessage = $"Sending {pagePaths.Count} page(s) x {NumberOfCopies} {(NumberOfCopies == 1 ? "copy" : "copies")} to {SelectedPrinter.Name}...";
            var success = await _printerService.PrintPagesAsync(SelectedPrinter.Name, pagePaths, NumberOfCopies);

            foreach (var row in recordsToPrint)
            {
                row.IsPrinted = success;
                if (!success) row.State = PrintRecordState.Failed;
                if (success) ApplyStatusMappings(row);
            }
            await PersistStatusesAsync();

            if (success && DigitalCopyMode != DigitalCopyMode.Off)
            {
                await SaveAutomaticDigitalCopyAsync(pagePaths, pdfPages);
            }

            StatusMessage = success
                ? $"Sent {recordsToPrint.Count} card(s) to {SelectedPrinter.Name}."
                : "The printer reported the job could not be sent. Check Preferences and try again.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Print failed: {ex.Message}";
        }
        finally
        {
            IsPrinting = false;
            if (tempDir is not null)
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { /* best-effort cleanup only */ }
            }
        }
    }

    /// <summary>Renders one card side into however many pages ColorMode calls for (one
    /// for Composite, one thresholded for MonoChrome, or both for
    /// CompositeAndMonoChrome -- matching a real ID-card printer's separate color/resin
    /// passes), applying Rotate 180 and Antialiasing="No" first when those are on.
    /// Returns each page as both a saved PNG (for PrintPagesAsync) and the raw pixel
    /// data already in hand for an optional PDF digital copy, so nothing gets rendered
    /// twice just to serve both.</summary>
    private async Task<List<(string Path, CardPdfWriter.PdfPage PdfPage)>> SavePageAsync(
        string tempDir, CardSide side, ThumbnailRenderer.RenderOptions options, int pageIndex, bool applyRotation)
    {
        var results = new List<(string, CardPdfWriter.PdfPage)>();
        var disableAntialiasing = AntialiasMode == PrintAntialiasMode.No;

        if (ColorMode is PrintColorMode.Composite or PrintColorMode.CompositeAndMonoChrome)
        {
            results.Add(await RenderAndSavePageAsync(tempDir, side, options, $"{pageIndex}_color", disableAntialiasing, applyRotation, monoChrome: false));
        }

        if (ColorMode is PrintColorMode.MonoChrome or PrintColorMode.CompositeAndMonoChrome)
        {
            results.Add(await RenderAndSavePageAsync(tempDir, side, options, $"{pageIndex}_mono", disableAntialiasing, applyRotation, monoChrome: true));
        }

        return results;
    }

    private async Task<(string Path, CardPdfWriter.PdfPage PdfPage)> RenderAndSavePageAsync(
        string tempDir, CardSide side, ThumbnailRenderer.RenderOptions options, string fileSuffix,
        bool disableAntialiasing, bool applyRotation, bool monoChrome)
    {
        var rendered = CardRasterizer.RenderSide(Document, side, _assetService, options, disableAntialiasing: disableAntialiasing);
        var bgra = CardRasterizer.ExtractBgraPixels(rendered);
        var width = rendered.PixelSize.Width;
        var height = rendered.PixelSize.Height;
        var rowBytes = bgra.Length / height;

        if (monoChrome)
        {
            ApplyMonoChrome(bgra, (byte)Math.Clamp(BlackPanelThreshold, 0, 255));
        }
        if (applyRotation)
        {
            CardRasterizer.Rotate180InPlace(bgra, width, height, rowBytes);
        }

        var bitmap = RewrapAsBitmap(bgra, rendered);
        var path = Path.Combine(tempDir, $"page_{fileSuffix}.png");
        await using (var stream = File.Create(path))
        {
            bitmap.Save(stream);
        }

        var pdfPage = new CardPdfWriter.PdfPage(bgra, width, height, Document.WidthMm, Document.HeightMm);
        return (path, pdfPage);
    }

    /// <summary>DigitalCopyMode's own automatic save -- distinct from the manual
    /// "Digital Copy" button (which always prompts for a location): writes straight to
    /// %AppData%/InfoID/DigitalCopies, one subfolder per print run, no prompt, since this
    /// is meant to run unattended as part of every print job that has it turned on.</summary>
    private async Task SaveAutomaticDigitalCopyAsync(IReadOnlyList<string> pagePaths, IReadOnlyList<CardPdfWriter.PdfPage> pdfPages)
    {
        var folder = Path.Combine(InfoID.Infrastructure.Persistence.DatabaseLocation.GetApplicationRoot(), "DigitalCopies",
            DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(folder);

        if (DigitalCopyMode == DigitalCopyMode.PdfFile)
        {
            var bytes = CardPdfWriter.Build(pdfPages);
            await File.WriteAllBytesAsync(Path.Combine(folder, $"{Document.Name}.pdf"), bytes);
        }
        else if (DigitalCopyMode == DigitalCopyMode.ImageFiles)
        {
            for (var i = 0; i < pagePaths.Count; i++)
            {
                File.Copy(pagePaths[i], Path.Combine(folder, $"page_{i + 1:D3}.png"), overwrite: true);
            }
        }
    }

    private static void ApplyMonoChrome(byte[] bgra, byte threshold)
    {
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var b = bgra[i]; var g = bgra[i + 1]; var r = bgra[i + 2];
            var gray = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
            var value = gray >= threshold ? (byte)255 : (byte)0;
            bgra[i] = value; bgra[i + 1] = value; bgra[i + 2] = value;
        }
    }

    /// <summary>Wraps a modified BGRA buffer (see ApplyMonoChrome) back into a real,
    /// savable bitmap -- same WriteableBitmap.Lock()+Marshal.Copy technique
    /// ImageEditingService.ProcessPixels already uses for exactly this "process pixels,
    /// then hand back a real Bitmap" purpose.</summary>
    private static WriteableBitmap RewrapAsBitmap(byte[] bgra, RenderTargetBitmap source)
    {
        var writeable = new WriteableBitmap(source.PixelSize, source.Dpi, PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = writeable.Lock();
        var byteCount = Math.Min(bgra.Length, fb.RowBytes * fb.Size.Height);
        Marshal.Copy(bgra, 0, fb.Address, byteCount);
        return writeable;
    }

    [RelayCommand]
    private void RefreshBlackPreview()
    {
        var options = BuildRenderOptions(PreviewRecord);
        var threshold = (byte)Math.Clamp(BlackPanelThreshold, 0, 255);

        BlackPreviewFront?.Dispose();
        BlackPreviewBack?.Dispose();

        BlackPreviewFront = RenderMonoChromePreview(CardSide.Front, options, threshold);
        BlackPreviewBack = RenderMonoChromePreview(CardSide.Back, options, threshold);
    }

    private Bitmap RenderMonoChromePreview(CardSide side, ThumbnailRenderer.RenderOptions options, byte threshold)
    {
        // A lower DPI than actual print output -- this is an on-screen preview, not the
        // file that gets sent to the printer, so there is no reason to pay 300 DPI's
        // render/pixel-processing cost every time the threshold slider moves.
        var rendered = CardRasterizer.RenderSide(Document, side, _assetService, options, dpi: 150);
        var bgra = CardRasterizer.ExtractBgraPixels(rendered);
        ApplyMonoChrome(bgra, threshold);
        return RewrapAsBitmap(bgra, rendered);
    }

    partial void OnPreviewRecordChanged(PreviewRecord? value) => RefreshBlackPreview();
    partial void OnBlackPanelThresholdChanged(double value) => RefreshBlackPreview();

    [RelayCommand]
    private async Task SaveDigitalCopy()
    {
        try
        {
            var path = await _filePickerService.PickSaveFileAsync(
                "Save Digital Copy", "PDF", new[] { "*.pdf" }, $"{Document.Name}_DigitalCopy", "pdf");
            if (path is null) return;

            var record = PreviewRecord ?? Records.FirstOrDefault()?.Record;
            var options = BuildRenderOptions(record);
            var pages = new List<CardPdfWriter.PdfPage>();

            if (PrintFront) pages.Add(BuildPdfPage(CardSide.Front, options));
            if (PrintBack) pages.Add(BuildPdfPage(CardSide.Back, options));
            if (pages.Count == 0) pages.Add(BuildPdfPage(CardSide.Front, options));

            var bytes = CardPdfWriter.Build(pages);
            await File.WriteAllBytesAsync(path, bytes);
            StatusMessage = $"Digital copy saved to {path}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't save digital copy: {ex.Message}";
        }
    }

    private CardPdfWriter.PdfPage BuildPdfPage(CardSide side, ThumbnailRenderer.RenderOptions options)
    {
        var rendered = CardRasterizer.RenderSide(Document, side, _assetService, options);
        var bgra = CardRasterizer.ExtractBgraPixels(rendered);
        return new CardPdfWriter.PdfPage(bgra, rendered.PixelSize.Width, rendered.PixelSize.Height, Document.WidthMm, Document.HeightMm);
    }

    [RelayCommand]
    private void Close() => RequestClose(true);
}
