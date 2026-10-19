using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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
///
/// Preview fix: BOTH the Color and the Black preview are now real bitmaps rendered by
/// CardRasterizer with BuildRenderOptions(PreviewRecord) -- the exact pipeline Print
/// uses -- and both re-render whenever PreviewRecord changes. Previously the Color
/// panel was drawn by the View calling ResolveText/ResolveField/etc. once, so it never
/// followed record navigation and never used the per-record image resolver.
/// </summary>
public sealed partial class PrintDialogViewModel : DialogViewModelBase<bool>
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<PrintDialogViewModel>();

    private readonly IPrinterService _printerService;
    private readonly IPrintStatusStore _statusStore;
    private readonly ICardFormatCatalogService _formatCatalogService;
    private readonly IDataBindingEvaluator _evaluator;
    private readonly IDesignAssetService _assetService;
    private readonly IFilePickerService _filePickerService;
    private readonly Func<ImageElement, IReadOnlyDictionary<string, string>, string?>? _resolveImagePath;
    /// <summary>Licensing (FR-LIC-12): printing needs a valid, unexpired license and
    /// consumes the card allowance. Null only for callers outside the designer (none
    /// today), which then print unlicensed-unchecked.</summary>
    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory? _licenseScopes;

    // Trial mode: who may print (license / trial / sign in first) and the on-screen state.
    private readonly InfoID.Desktop.Features.Trial.Services.PrintAuthorizationService? _printAuthorization;
    /// <summary>The licensed feature this print uses: Printing (counts toward the card
    /// limit) or VisitorBadges (visitor badges: valid license needed, not counted).</summary>
    private readonly string _licensedFeature;
    private readonly InfoID.Desktop.Features.Trial.Services.TrialModeState? _trialState;

    private static readonly IReadOnlyDictionary<string, string> EmptyFields = new Dictionary<string, string>();

    /// <summary>On-screen preview resolution. Lower than print DPI on purpose: this is
    /// re-rendered on every record change / threshold tick, not sent to the printer.</summary>
    private const int PreviewDpi = 150;

    public override string Title => "Print";
    public override bool CanResize => true;
    /// <summary>Standard working size for the Print dialog. DialogService clamps this to
    /// the main window so it never opens larger than the screen the app is on.</summary>
    public override (double Width, double Height)? PreferredSize => (1180, 820);

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

    /// <summary>Off (the default): Print sends the cards straight to the printer chosen
    /// in this dialog, with no second window -- the standard workflow in professional
    /// ID card software. On: the Windows print dialog opens first. Remembered between
    /// sessions like the other print settings.</summary>
    [ObservableProperty]
    private bool _showSystemPrintDialog;

    // ---------------------------------------------------------------- print range/copies ----

    [ObservableProperty]
    private int _numberOfCards = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomRange))]
    [NotifyPropertyChangedFor(nameof(RangeSummary))]
    [NotifyPropertyChangedFor(nameof(HasRangeError))]
    private PrintRangeMode _rangeMode = PrintRangeMode.AllRecords;

    /// <summary>Custom record list, e.g. "1-3, 5, 8" (1-based, as numbered in the
    /// Advanced Print Operations record list).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeSummary))]
    [NotifyPropertyChangedFor(nameof(HasRangeError))]
    private string _customRangeText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeSummary))]
    private PageParityFilter _pageParity = PageParityFilter.AllPages;

    public ObservableCollection<PageParityFilter> PageParityOptions { get; } = new()
    {
        PageParityFilter.AllPages, PageParityFilter.OddPages, PageParityFilter.EvenPages,
    };

    public bool IsCustomRange => RangeMode == PrintRangeMode.CustomRange;

    public bool HasRangeError => RangeMode == PrintRangeMode.CustomRange && !TryParseRange(CustomRangeText, Records.Count, out _, out _);

    /// <summary>"Prints 3 cards (6 pages) x 1 copy", or the reason the custom range is
    /// invalid -- updated live so the user sees exactly what Print will send.</summary>
    public string RangeSummary
    {
        get
        {
            if (RangeMode == PrintRangeMode.CustomRange &&
                !TryParseRange(CustomRangeText, Records.Count, out _, out var error))
            {
                return error ?? "Invalid range.";
            }

            var cards = GetRecordsToPrint().Count;
            var pagesPerCard = (PrintFront ? 1 : 0) + (PrintBack ? 1 : 0);
            if (ColorMode == PrintColorMode.CompositeAndMonoChrome) pagesPerCard *= 2;
            var pages = cards * pagesPerCard;
            pages = PageParity switch
            {
                PageParityFilter.OddPages => (pages + 1) / 2,
                PageParityFilter.EvenPages => pages / 2,
                _ => pages,
            };

            return $"Prints {cards} card{(cards == 1 ? "" : "s")} ({pages} page{(pages == 1 ? "" : "s")}) x {NumberOfCopies} {(NumberOfCopies == 1 ? "copy" : "copies")}";
        }
    }

    partial void OnNumberOfCopiesChanged(int value) => OnPropertyChanged(nameof(RangeSummary));
    partial void OnPrintFrontChanged(bool value) => OnPropertyChanged(nameof(RangeSummary));
    partial void OnPrintBackChanged(bool value) => OnPropertyChanged(nameof(RangeSummary));
    partial void OnColorModeChanged(PrintColorMode value) => OnPropertyChanged(nameof(RangeSummary));

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
    private PrintColorMode _colorMode = PrintColorMode.Composite;

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
    [NotifyPropertyChangedFor(nameof(PreviewPositionText))]
    private PreviewRecord? _previewRecord;

    /// <summary>"Record 3 of 20" -- lets the user see the preview actually moved.</summary>
    public string PreviewPositionText
    {
        get
        {
            if (Records.Count == 0 || PreviewRecord is null) return "No records";
            var index = Records.ToList().FindIndex(r => r.Record == PreviewRecord);
            return $"Record {index + 1} of {Records.Count}";
        }
    }

    /// <summary>Real rendered color bitmaps for the Preview tab's "Color" sub-tab --
    /// same CardRasterizer + BuildRenderOptions(PreviewRecord) pipeline Print uses, so
    /// text, data fields, visibility rules AND per-record images all follow the
    /// currently previewed record.</summary>
    [ObservableProperty]
    private Bitmap? _colorPreviewFront;

    [ObservableProperty]
    private Bitmap? _colorPreviewBack;

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

    // ---------------------------------------------------------------- card records (print history) ----

    /// <summary>True when the run prints real database/spreadsheet rows. False for a
    /// static design (sample preview data), which is recorded as generic cards.</summary>
    public bool IsDataBound { get; }

    /// <summary>Data columns for the Person ID / Name / Expiry pickers, with "(none)" first.</summary>
    public ObservableCollection<string> HistoryColumnOptions { get; } = new();

    public IReadOnlyList<CardValidityOption> ValidityOptions { get; } =
        Enum.GetValues<CardValidityMode>().Select(m => new CardValidityOption(m, PrintHistoryPlanner.ValidityLabel(m))).ToList();

    /// <summary>Column that identifies the person, so printing the same person again with
    /// this design is recognized as a reprint (Lost / Damaged / Renewed).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardRecordsSummary))]
    [NotifyPropertyChangedFor(nameof(HasPersonIdColumn))]
    private string _personIdColumn = PrintHistoryPlanner.NoColumn;

    [ObservableProperty]
    private string _nameColumn = PrintHistoryPlanner.NoColumn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsValidUntilDate))]
    [NotifyPropertyChangedFor(nameof(IsValidityFromColumn))]
    [NotifyPropertyChangedFor(nameof(CardRecordsSummary))]
    private CardValidityOption? _selectedValidity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardRecordsSummary))]
    private DateTime? _validUntilDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CardRecordsSummary))]
    private string _expiryColumn = PrintHistoryPlanner.NoColumn;

    public bool HasPersonIdColumn => IsColumn(PersonIdColumn);
    public bool IsValidUntilDate => SelectedValidity?.Mode == CardValidityMode.UntilDate;
    public bool IsValidityFromColumn => SelectedValidity?.Mode == CardValidityMode.FromColumn;

    /// <summary>One line telling the operator exactly what Print history will record.</summary>
    public string CardRecordsSummary
    {
        get
        {
            var validity = SelectedValidity?.Mode switch
            {
                CardValidityMode.OneYear or CardValidityMode.TwoYears or CardValidityMode.ThreeYears =>
                    $"valid until {PrintHistoryPlanner.ComputeExpiry(BuildHistorySettings(), EmptyFields, DateTime.Today):d}",
                CardValidityMode.UntilDate => ValidUntilDate is { } until ? $"valid until {until:d}" : "pick the expiry date",
                CardValidityMode.FromColumn => IsColumn(ExpiryColumn) ? $"expiry from \"{ExpiryColumn}\"" : "pick the expiry column",
                _ => "no expiry date",
            };

            if (!IsDataBound)
                return $"Each printed card is recorded in Print history ({validity}).";

            return HasPersonIdColumn
                ? $"Cards are recorded per person by \"{PersonIdColumn}\" ({validity}). Printing someone again asks for a reprint reason."
                : $"Cards are recorded ({validity}), but without a Person ID column reprints can't be recognized.";
        }
    }

    /// <summary>Shown over the dialog when people in the run already have a card.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReprintPromptOpen))]
    private ReprintPromptViewModel? _reprintPrompt;

    public bool IsReprintPromptOpen => ReprintPrompt is not null;

    private string _historyDesignKey = string.Empty;
    private bool _historySettingsLoaded;

    private static bool IsColumn(string? column) =>
        !string.IsNullOrWhiteSpace(column) && column != PrintHistoryPlanner.NoColumn;

    public PrintDialogViewModel(
        CardDesignDocument document,
        IPrinterService printerService,
        IPrintStatusStore statusStore,
        ICardFormatCatalogService formatCatalogService,
        IPreviewDataProvider previewDataProvider,
        IDataBindingEvaluator evaluator,
        IDesignAssetService assetService,
        IFilePickerService filePickerService,
        IReadOnlyList<PreviewRecord>? databaseRecords = null,
        Func<ImageElement, IReadOnlyDictionary<string, string>, string?>? resolveImagePath = null,
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory? licenseScopes = null,
        InfoID.Desktop.Features.Trial.Services.PrintAuthorizationService? printAuthorization = null,
        InfoID.Desktop.Features.Trial.Services.TrialModeState? trialState = null,
        string licensedFeature = InfoID.Application.Services.LicensedFeatures.Printing)
    {
        _licensedFeature = licensedFeature;
        _printAuthorization = printAuthorization;
        _trialState = trialState;
        if (_trialState is not null)
        {
            _trialState.Changed += OnTrialStateChanged;
            _ = _trialState.RefreshAsync();
        }
        Document = document;
        _printerService = printerService;
        _statusStore = statusStore;
        _formatCatalogService = formatCatalogService;
        _evaluator = evaluator;
        _assetService = assetService;
        _filePickerService = filePickerService;
        _resolveImagePath = resolveImagePath;
        _licenseScopes = licenseScopes;

        if (_resolveImagePath is null)
        {
            // Diagnostic for the "photo never changes per record" symptom: without this
            // resolver every ImageElement falls back to its design-time AssetReference.
            Log.Warn("resolveImagePath was not supplied: " +
                            "database-bound photos will show the design-time image for every record.");
        }

        PrintingSupportedHere = printerService.IsSupported;
        StatusMessage = PrintingSupportedHere
            ? null
            : "No supported print mechanism was found on this system -- printer selection and Print will be unavailable, but Preview and Digital Copy still work.";

        SelectedOrientationOption = document.Orientation == CardOrientation.Portrait ? "Portrait" : "Landscape";

        var records = databaseRecords is { Count: > 0 }
          ? databaseRecords
          : previewDataProvider.GetSampleRecords();
        foreach (var key in records.SelectMany(r => r.Fields.Keys).Distinct())
        {
            AvailableFieldKeys.Add(key);
        }
        StatusMappings.Add(new PrintStatusMappingRow());

        IsDataBound = databaseRecords is { Count: > 0 };
        HistoryColumnOptions.Add(PrintHistoryPlanner.NoColumn);
        foreach (var key in AvailableFieldKeys) HistoryColumnOptions.Add(key);
        SelectedValidity = ValidityOptions[0];
        _historyDesignKey = PrintHistoryPlanner.BuildDesignKey(document.PersistedTemplateId, document.SourceFilePath, document.Name);

        NumberOfCards = Math.Max(1, records.Count);

        _ = InitializeAsync(records);
    }

    private async Task InitializeAsync(IReadOnlyList<PreviewRecord> records)
    {
        await LoadRememberedSettingsAsync();
        await LoadHistorySettingsAsync();

        var statuses = await _statusStore.LoadAsync();
        foreach (var record in records)
        {
            statuses.TryGetValue(record.Label, out var status);
            Records.Add(new PrintQueueRow(record, status));
        }

        // Setting PreviewRecord triggers OnPreviewRecordChanged -> RefreshAllPreviews.
        // If there are no records it stays null and no change fires, so render the
        // design-time card explicitly in that case.
        PreviewRecord = Records.FirstOrDefault()?.Record;
        if (PreviewRecord is null) RefreshAllPreviews();
        OnPropertyChanged(nameof(PreviewPositionText));
        OnPropertyChanged(nameof(RangeSummary));
        OnPropertyChanged(nameof(HasRangeError));

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

    public string ResolveText(TextElement text) => ResolveTextFor(text, PreviewRecord?.Fields ?? EmptyFields);

    /// <summary>Text bound to a database column (Parameters tab, Database) shows that
    /// column's value; otherwise the binding expression or the fixed text, as before.</summary>
    private string ResolveTextFor(TextElement text, IReadOnlyDictionary<string, string> fields)
    {
        if (text.IsDatabaseBound && !string.IsNullOrWhiteSpace(text.DatabaseColumn))
            return fields.TryGetValue(text.DatabaseColumn, out var value) ? value : string.Empty;

        return string.IsNullOrEmpty(text.DataBindingExpression)
            ? text.Text
            : _evaluator.Evaluate(text.DataBindingExpression, fields);
    }

    public string ResolveField(DataFieldElement field) =>
        _evaluator.Evaluate($"{{{{{field.FieldKey}}}}}", PreviewRecord?.Fields ?? EmptyFields);

    public bool IsVisibleNow(DesignerElement element) =>
        _evaluator.EvaluateCondition(element.VisibilityCondition, PreviewRecord?.Fields ?? EmptyFields);

    /// <summary>Render options for one record. <paramref name="trialWatermark"/>: for the
    /// printed/saved output it comes from the print authorization; for the on-screen
    /// previews (null) from the current mode -- trial users see what will print.</summary>
    private ThumbnailRenderer.RenderOptions BuildRenderOptions(PreviewRecord? record, bool? trialWatermark = null)
    {
        var fields = record?.Fields ?? EmptyFields;

        return new()
        {
            TrialWatermark = trialWatermark ?? _trialState?.IsTrialMode == true,
            ResolveAssetPath = _assetService.ResolveToFullPath,
            ResolveText = t => ResolveTextFor(t, fields),
            ResolveField = f => _evaluator.Evaluate($"{{{{{f.FieldKey}}}}}", fields),
            IsVisibleNow = e => _evaluator.EvaluateCondition(e.VisibilityCondition, fields),
            ResolveImagePath = image => _resolveImagePath?.Invoke(image, fields),
            // QR codes / barcodes bound to a field ("{{VisitorId}}") encode this record's value.
            ResolveCodeValue = element => InfoID.Desktop.Features.CardDesigner.Services.CodeValueBinding.Resolve(element, fields, _evaluator),
        };
    }

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

        // Prefer a real printer over "virtual" ones (Microsoft Print to PDF, XPS,
        // OneNote, Fax): when Windows' default is Print to PDF, pressing Print used to
        // silently produce a PDF save prompt instead of a printed card.
        SelectedPrinter = Printers.FirstOrDefault(p => p.Name == previouslySelected)
                           ?? Printers.FirstOrDefault(p => p.Name == _rememberedPrinterName)
                           ?? Printers.FirstOrDefault(p => p.IsDefault && !IsVirtualPrinter(p.Name))
                           ?? Printers.FirstOrDefault(p => !IsVirtualPrinter(p.Name) && p.IsOnline)
                           ?? Printers.FirstOrDefault(p => p.IsDefault)
                           ?? Printers.FirstOrDefault();

        if (Printers.Count == 0)
        {
            StatusMessage = "No printers were found. Make sure a printer is installed on this system.";
        }
    }

    private static readonly string[] VirtualPrinterKeywords =
        { "PDF", "XPS", "OneNote", "Fax", "Send To", "Snagit", "Document Writer" };

    /// <summary>True for software printers that save a file instead of printing.</summary>
    internal static bool IsVirtualPrinter(string name) =>
        VirtualPrinterKeywords.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));

    /// <summary>Shown under the printer list when a file-saving printer is selected, so
    /// the user knows why Print will ask where to save a file.</summary>
    public bool IsVirtualPrinterSelected => SelectedPrinter is not null && IsVirtualPrinter(SelectedPrinter.Name);

    partial void OnSelectedPrinterChanged(PrinterInfo? value) => OnPropertyChanged(nameof(IsVirtualPrinterSelected));

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

    // ---------------------------------------------------------------- remembered settings ----

    private string? _rememberedPrinterName;
    private bool _settingsLoaded;

    private static readonly HashSet<string> RememberedProperties = new()
    {
        nameof(SelectedPrinter), nameof(ShowSystemPrintDialog), nameof(PrintFront), nameof(PrintBack),
        nameof(NumberOfCopies), nameof(ReversedOrder), nameof(ColorMode), nameof(AntialiasMode),
        nameof(BlackPanelThreshold), nameof(Rotate180), nameof(Rotate180Landscape), nameof(Rotate180Portrait),
        nameof(DigitalCopyMode), nameof(PageParity),
    };

    /// <summary>Restores the printer and output settings from the last session, then
    /// starts saving any change to them. Saving is enabled only after loading so the
    /// defaults applied while the dialog opens never overwrite the saved file.</summary>
    private async Task LoadRememberedSettingsAsync()
    {
        var saved = await PrintSettingsStore.Shared.LoadAsync();

        _rememberedPrinterName = saved.PrinterName;
        ShowSystemPrintDialog = saved.ShowSystemPrintDialog;
        PrintFront = saved.PrintFront;
        PrintBack = saved.PrintBack;
        NumberOfCopies = Math.Clamp(saved.NumberOfCopies, 1, 999);
        ReversedOrder = saved.ReversedOrder;
        ColorMode = saved.ColorMode;
        AntialiasMode = saved.AntialiasMode;
        BlackPanelThreshold = Math.Clamp(saved.BlackPanelThreshold, 0, 255);
        Rotate180 = saved.Rotate180;
        Rotate180Landscape = saved.Rotate180Landscape;
        Rotate180Portrait = saved.Rotate180Portrait;
        DigitalCopyMode = saved.DigitalCopyMode;
        PageParity = saved.PageParity;

        _settingsLoaded = true;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null && RememberedProperties.Contains(e.PropertyName))
                _ = SaveRememberedSettingsAsync();
        };
    }

    private Task SaveRememberedSettingsAsync()
    {
        if (!_settingsLoaded) return Task.CompletedTask;

        return PrintSettingsStore.Shared.SaveAsync(new PrintSettings
        {
            // Keep the remembered printer when the list is temporarily empty (e.g. a
            // printer that is switched off), instead of forgetting it.
            PrinterName = SelectedPrinter?.Name ?? _rememberedPrinterName,
            ShowSystemPrintDialog = ShowSystemPrintDialog,
            PrintFront = PrintFront,
            PrintBack = PrintBack,
            NumberOfCopies = NumberOfCopies,
            ReversedOrder = ReversedOrder,
            ColorMode = ColorMode,
            AntialiasMode = AntialiasMode,
            BlackPanelThreshold = BlackPanelThreshold,
            Rotate180 = Rotate180,
            Rotate180Landscape = Rotate180Landscape,
            Rotate180Portrait = Rotate180Portrait,
            DigitalCopyMode = DigitalCopyMode,
            PageParity = PageParity,
        });
    }

    // ---------------------------------------------------------------- trial mode ----

    /// <summary>Trial mode banner: signed in without a license.</summary>
    public bool ShowTrialBanner => _trialState?.IsTrialMode == true;

    /// <summary>No license and not signed in: printing needs a login.</summary>
    public bool ShowSignInBanner => _trialState?.NeedsSignIn == true;

    public bool TrialUsedUp => _trialState?.TrialUsedUp == true;

    /// <summary>"Trial mode · 3 of 10 trial cards used · 7 left".</summary>
    public string TrialBannerText
    {
        get
        {
            if (_trialState is not { } t) return string.Empty;
            if (t.TrialUsedUp) return $"You've used all {t.CardLimit} trial cards. Activate InfoID to keep printing. You can still design, save and preview cards.";
            var counts = t.HasTrialStatus ? $"{t.TrialUsageText} · {t.TrialRemainingText}" : "checking your trial cards...";
            var offline = t.TrialError is null ? "" : " (couldn't reach the server: printing needs an internet connection)";
            return $"Trial mode · {counts}{offline}. Printed cards carry an \"{InfoID.Desktop.Features.Trial.Services.TrialWatermark.Text}\" watermark.";
        }
    }

    public string SignInBannerText =>
        $"InfoID isn't activated. Log in to start your free trial and print up to {_trialState?.CardLimit ?? InfoID.Desktop.Features.Trial.Services.TrialModeState.DefaultCardLimit} cards, or activate InfoID.";

    private void OnTrialStateChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ShowTrialBanner));
        OnPropertyChanged(nameof(ShowSignInBanner));
        OnPropertyChanged(nameof(TrialUsedUp));
        OnPropertyChanged(nameof(TrialBannerText));
        OnPropertyChanged(nameof(SignInBannerText));
        // The previews show the watermark exactly when a print would carry it.
        if (_previewWatermark != (_trialState?.IsTrialMode == true)) RefreshAllPreviews();
    }

    private bool _previewWatermark;

    /// <summary>Shown over the dialog when printing needs a login first.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoginPromptOpen))]
    private InfoID.Desktop.Features.Trial.ViewModels.TrialLoginPromptViewModel? _loginPrompt;

    public bool IsLoginPromptOpen => LoginPrompt is not null;

    [RelayCommand]
    private Task ShowLogin() => PromptLoginAsync(null);

    /// <summary>"Please log in to start your trial..." with Log in / Cancel. After a login
    /// the license and trial are checked again; printing then needs a new click on Print
    /// (with the watermark now visible in the preview).</summary>
    private async Task PromptLoginAsync(string? message)
    {
        if (_trialState is null || LoginPrompt is not null) return;
        var services = InfoID.Desktop.App.Services;
        var session = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<InfoID.Desktop.Features.Account.Services.AccountSession>(services);
        var portal = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetRequiredService<InfoID.Desktop.Features.Account.Services.CustomerPortalClient>(services);

        var prompt = new InfoID.Desktop.Features.Trial.ViewModels.TrialLoginPromptViewModel(session, portal, _trialState.CardLimit, message);
        LoginPrompt = prompt;
        var result = await prompt.Completion;
        LoginPrompt = null;

        switch (result)
        {
            case InfoID.Desktop.Features.Trial.ViewModels.TrialLoginResult.SignedIn:
                StatusMessage = "Logged in. Checking your license and trial...";
                await _trialState.RefreshAsync();
                RefreshAllPreviews();
                StatusMessage = _trialState.IsActivated
                    ? "Logged in. InfoID is activated on this computer: click Print to print."
                    : _trialState.TrialUsedUp
                        ? $"Logged in. You've used all {_trialState.CardLimit} trial cards. Activate InfoID to keep printing."
                        : $"Logged in. Trial mode: {_trialState.TrialUsageText}. The preview shows the trial watermark; click Print to print.";
                break;
            case InfoID.Desktop.Features.Trial.ViewModels.TrialLoginResult.OpenAccountPage:
                OpenAccountPage(InfoID.Desktop.Features.Account.ViewModels.AccountPanel.LogIn);
                break;
            default:
                StatusMessage = "Printing needs a login or an activated license. You can keep designing and previewing.";
                break;
        }
    }

    /// <summary>Activate / buy: closes the Print window and opens Account and license.</summary>
    [RelayCommand]
    private void Activate() => OpenAccountPage(null);

    private void OpenAccountPage(InfoID.Desktop.Features.Account.ViewModels.AccountPanel? panel)
    {
        Close();
        var navigation = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<InfoID.Desktop.Core.Navigation.INavigationService>(InfoID.Desktop.App.Services);
        navigation?.NavigateTo<InfoID.Desktop.Features.Account.ViewModels.AccountLicenseViewModel>(panel);
    }

    /// <summary>Asks permission for a job of <paramref name="cards"/> cards. Null = allowed
    /// without a print gate (no services available: old behaviour, license check only).</summary>
    private async Task<InfoID.Desktop.Features.Trial.Services.PrintAuthorization?> AuthorizeAsync(int cards)
    {
        if (_printAuthorization is null) return null;
        StatusMessage = _trialState?.IsTrialMode == true ? "Reserving trial cards..." : StatusMessage;
        return await _printAuthorization.AuthorizeAsync(cards, Document.Name, feature: _licensedFeature);
    }

    /// <summary>Explains a refusal; for "log in first" opens the login prompt.</summary>
    private async Task HandleDenialAsync(InfoID.Desktop.Features.Trial.Services.PrintAuthorization authorization)
    {
        StatusMessage = authorization.Message;
        if (authorization.Denial == InfoID.Desktop.Features.Trial.Services.PrintDenial.SignInRequired)
            await PromptLoginAsync(authorization.Message);
        else if (_trialState is not null)
            await _trialState.RefreshAsync();
    }

    /// <summary>Null when printing is allowed, otherwise the reason to show. Only used
    /// when no print gate is available (see AuthorizeAsync).</summary>
    private async Task<string?> CheckLicenseAsync(int cardCount)
    {
        if (_licenseScopes is null) return null;
        try
        {
            using var scope = _licenseScopes.CreateScope();
            var license = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<InfoID.Application.Interfaces.ILicenseService>(scope.ServiceProvider);
            var check = await license.CheckFeatureAllowedAsync(_licensedFeature, cardCount);
            return check.IsAllowed ? null : check.Reason;
        }
        catch (Exception ex)
        {
            return "The license could not be checked: " + ex.GetBaseException().Message;
        }
    }

    private async Task CountPrintedCardsAsync(int cardCount)
    {
        if (_licenseScopes is null || cardCount <= 0) return;
        if (!InfoID.Application.Services.LicensedFeatures.CountsCards(_licensedFeature)) return; // visitor badges
        try
        {
            using var scope = _licenseScopes.CreateScope();
            var license = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
                .GetRequiredService<InfoID.Application.Interfaces.ILicenseService>(scope.ServiceProvider);
            await license.IncrementCardsIssuedAsync(cardCount);
        }
        catch (Exception ex)
        {
            // The cards are already printed; don't turn a counting problem into a
            // print failure message.
            Log.Warn("License card count failed", ex);
        }
    }

    // ---------------------------------------------------------------- print history ----

    private static readonly HashSet<string> HistoryProperties = new()
    {
        nameof(PersonIdColumn), nameof(NameColumn), nameof(SelectedValidity), nameof(ValidUntilDate), nameof(ExpiryColumn),
    };

    /// <summary>Restores this design's Person ID / name / validity choices, or detects the
    /// columns the first time (EmployeeNo, RollNo, Name...). A remembered column that no
    /// longer exists in the data is detected again rather than kept.</summary>
    private async Task LoadHistorySettingsAsync()
    {
        var saved = await PrintHistorySettingsStore.Shared.LoadAsync(_historyDesignKey);
        var columns = AvailableFieldKeys.ToList();

        string Pick(string? remembered, Func<IEnumerable<string>, string?> detect)
        {
            if (remembered == PrintHistoryPlanner.NoColumn) return PrintHistoryPlanner.NoColumn;
            if (remembered is not null && columns.Contains(remembered)) return remembered;
            return detect(columns) ?? PrintHistoryPlanner.NoColumn;
        }

        if (IsDataBound)
        {
            PersonIdColumn = Pick(saved?.PersonIdColumn, PrintHistoryPlanner.DetectPersonIdColumn);
            NameColumn = Pick(saved?.NameColumn, PrintHistoryPlanner.DetectNameColumn);
            ExpiryColumn = Pick(saved?.ExpiryColumn, PrintHistoryPlanner.DetectExpiryColumn);
        }

        var mode = saved?.Validity ?? CardValidityMode.NoExpiry;
        // "From a data column" means nothing without data; fall back to no expiry.
        if (mode == CardValidityMode.FromColumn && !IsDataBound) mode = CardValidityMode.NoExpiry;
        SelectedValidity = ValidityOptions.FirstOrDefault(o => o.Mode == mode) ?? ValidityOptions[0];
        ValidUntilDate = saved?.ValidUntil;

        _historySettingsLoaded = true;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null && HistoryProperties.Contains(e.PropertyName))
                _ = PrintHistorySettingsStore.Shared.SaveAsync(_historyDesignKey, BuildHistorySettings());
        };
    }

    partial void OnSelectedValidityChanged(CardValidityOption? value)
    {
        // Suggest a sensible date the first time "Until a date" is picked.
        if (_historySettingsLoaded && value?.Mode == CardValidityMode.UntilDate && ValidUntilDate is null)
            ValidUntilDate = DateTime.Today.AddYears(1);
    }

    private PrintHistorySettings BuildHistorySettings() => new()
    {
        PersonIdColumn = PersonIdColumn,
        NameColumn = NameColumn,
        Validity = SelectedValidity?.Mode ?? CardValidityMode.NoExpiry,
        ValidUntil = ValidUntilDate?.Date,
        ExpiryColumn = ExpiryColumn,
    };

    private string? PersonKeyOf(PreviewRecord record)
    {
        if (!IsDataBound || !IsColumn(PersonIdColumn)) return null;
        return record.Fields.TryGetValue(PersonIdColumn, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private string DisplayNameOf(PreviewRecord record, string? personKey)
    {
        if (!IsDataBound) return string.IsNullOrWhiteSpace(Document.Name) ? "Card" : Document.Name;
        if (IsColumn(NameColumn) && record.Fields.TryGetValue(NameColumn, out var name) && !string.IsNullOrWhiteSpace(name))
            return name.Trim();
        return personKey ?? record.Label;
    }

    /// <summary>Runs one call against IPrintHistoryService in its own DI scope (the
    /// dialog outlives any single DbContext). Null when no service is available.</summary>
    private async Task<T?> WithHistoryAsync<T>(Func<InfoID.Application.Interfaces.IPrintHistoryService, Task<T>> action)
    {
        if (_licenseScopes is null) return default;
        using var scope = _licenseScopes.CreateScope();
        var service = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<InfoID.Application.Interfaces.IPrintHistoryService>(scope.ServiceProvider);
        return service is null ? default : await action(service);
    }

    /// <summary>Before printing: if people in the run already have a card for this
    /// design, asks whether to reprint them (and why), print only the new people, or
    /// cancel. Returns the rows to print (null = cancelled) and the reprint decision.</summary>
    private async Task<(List<PrintQueueRow>? Rows, ReprintDecision? Decision)> ConfirmReprintsAsync(List<PrintQueueRow> rows)
    {
        if (!IsDataBound || !HasPersonIdColumn) return (rows, null);

        var keys = rows.Select(r => PersonKeyOf(r.Record)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (keys.Count == 0) return (rows, null);

        IReadOnlyList<InfoID.Application.Dtos.PrintingModule.ExistingCardDto>? existing;
        try
        {
            existing = await WithHistoryAsync(s => s.FindExistingCardsAsync(Document.PersistedTemplateId, keys));
        }
        catch (Exception ex)
        {
            // History is a record of printing, never a gate on it.
            Log.Warn("Print history lookup failed", ex);
            return (rows, null);
        }
        if (existing is not { Count: > 0 }) return (rows, null);

        var existingKeys = existing.Select(e => e.PersonKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool IsExisting(PrintQueueRow row) => PersonKeyOf(row.Record) is { } key && existingKeys.Contains(key);
        var newRows = rows.Where(r => !IsExisting(r)).ToList();

        var prompt = new ReprintPromptViewModel(existing, newRows.Count);
        ReprintPrompt = prompt;
        var previousStatus = StatusMessage;
        StatusMessage = "Some people already have a card. Choose what to do.";
        var decision = await prompt.Completion;
        ReprintPrompt = null;
        StatusMessage = previousStatus;

        return decision.Choice switch
        {
            ReprintChoice.ReprintAll => (rows, decision),
            ReprintChoice.SkipExisting => (newRows, null),
            _ => (null, null),
        };
    }

    /// <summary>After printing: records the run in Print history (PrintJob, Card,
    /// PrintJobItem, CardStatusHistory). Returns a short note for the status line, or
    /// null. Never throws -- the cards are already printed.</summary>
    private async Task<string?> RecordPrintHistoryAsync(
        IReadOnlyList<PrintQueueRow> rows, bool succeeded, string? error, string printerName,
        DateTime startedUtc, ReprintDecision? decision, int? copiesPerRecord = null)
    {
        try
        {
            var settings = BuildHistorySettings();
            var request = new InfoID.Application.Dtos.PrintingModule.RecordPrintJobRequest
            {
                DesignName = Document.Name,
                TemplateId = Document.PersistedTemplateId,
                PrinterName = printerName,
                IsDataBound = IsDataBound,
                CopiesPerRecord = copiesPerRecord ?? NumberOfCopies,
                Succeeded = succeeded,
                FailureMessage = error,
                StartedUtc = startedUtc,
                ReprintReason = decision?.Reason,
                ReprintNote = decision?.Note,
            };
            foreach (var row in rows)
            {
                var key = PersonKeyOf(row.Record);
                request.Records.Add(new InfoID.Application.Dtos.PrintingModule.PrintedRecordInput
                {
                    PersonKey = key,
                    DisplayName = DisplayNameOf(row.Record, key),
                    ExpiryDate = PrintHistoryPlanner.ComputeExpiry(settings, row.Record.Fields, DateTime.Today),
                    ExpiryFromData = settings.Validity == CardValidityMode.FromColumn,
                });
            }

            var result = await WithHistoryAsync(s => s.RecordJobAsync(request));
            if (result is null || !succeeded) return null;

            var parts = new List<string>();
            if (result.NewCards > 0) parts.Add($"{result.NewCards} new card{(result.NewCards == 1 ? "" : "s")}");
            if (result.Reprints > 0) parts.Add($"{result.Reprints} reprint{(result.Reprints == 1 ? "" : "s")}");
            return parts.Count == 0 ? null : "Recorded " + string.Join(", ", parts) + " in Print history.";
        }
        catch (Exception ex)
        {
            Log.Error("Print history couldn't be recorded", ex);
            return "The cards printed, but Print history couldn't be updated: " + ex.GetBaseException().Message;
        }
    }

    // ---------------------------------------------------------------- range helpers ----

    /// <summary>The records the current Print Range selects, in print order.</summary>
    private List<PrintQueueRow> GetRecordsToPrint()
    {
        List<PrintQueueRow> rows;

        switch (RangeMode)
        {
            case PrintRangeMode.CurrentRecord:
                rows = Records.Where(r => r.Record == PreviewRecord).Take(1).ToList();
                if (rows.Count == 0 && Records.Count > 0) rows.Add(Records[0]);
                break;

            case PrintRangeMode.CustomRange:
                rows = TryParseRange(CustomRangeText, Records.Count, out var indexes, out _)
                    ? indexes.Select(i => Records[i]).ToList()
                    : new List<PrintQueueRow>();
                break;

            default:
                rows = Records.ToList();
                break;
        }

        if (ReversedOrder) rows.Reverse();
        return rows;
    }

    /// <summary>Parses "1-3, 5, 8" (1-based, commas or semicolons) into zero-based record
    /// indexes, in the order written, without duplicates. Returns false with a
    /// user-facing message when the text is empty, malformed or out of range.</summary>
    internal static bool TryParseRange(string? text, int recordCount, out List<int> indexes, out string? error)
    {
        indexes = new List<int>();
        error = null;

        if (recordCount == 0)
        {
            error = "There are no records to print.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            error = $"Enter records to print, e.g. 1-3, 5 (1 to {recordCount}).";
            return false;
        }

        var seen = new HashSet<int>();
        foreach (var rawPart in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.Trim();
            if (part.Length == 0) continue;

            int from, to;
            var dash = part.IndexOf('-');
            if (dash >= 0)
            {
                if (!int.TryParse(part[..dash].Trim(), out from) || !int.TryParse(part[(dash + 1)..].Trim(), out to))
                {
                    error = $"\"{part}\" isn't a valid range. Use e.g. 2-5.";
                    return false;
                }
            }
            else
            {
                if (!int.TryParse(part, out from))
                {
                    error = $"\"{part}\" isn't a record number.";
                    return false;
                }
                to = from;
            }

            if (from < 1 || to < 1 || from > recordCount || to > recordCount)
            {
                error = $"\"{part}\" is outside 1 to {recordCount}.";
                return false;
            }

            var step = from <= to ? 1 : -1;
            for (var n = from; n != to + step; n += step)
            {
                if (seen.Add(n - 1)) indexes.Add(n - 1);
            }
        }

        if (indexes.Count == 0)
        {
            error = $"Enter records to print, e.g. 1-3, 5 (1 to {recordCount}).";
            return false;
        }
        return true;
    }

    /// <summary>Keeps only odd or even pages (1-based, counted across the run).</summary>
    private static List<T> ApplyParity<T>(IReadOnlyList<T> pages, PageParityFilter parity) => parity switch
    {
        PageParityFilter.OddPages => pages.Where((_, i) => i % 2 == 0).ToList(),
        PageParityFilter.EvenPages => pages.Where((_, i) => i % 2 == 1).ToList(),
        _ => pages.ToList(),
    };

    [RelayCommand]
    private async Task Print()
    {
        if (!_printerService.IsSupported)
        {
            StatusMessage = "Printing isn't available on this system.";
            return;
        }
        var useSystemDialog = ShowSystemPrintDialog && _printerService.SupportsSystemPrintDialog;
        if (SelectedPrinter is null && !useSystemDialog)
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

        if (RangeMode == PrintRangeMode.CustomRange &&
            !TryParseRange(CustomRangeText, Records.Count, out _, out var rangeError))
        {
            StatusMessage = rangeError;
            return;
        }

        IsPrinting = true;
        StatusMessage = "Preparing pages...";
        string? tempDir = null;
        InfoID.Desktop.Features.Trial.Services.PrintAuthorization? authorization = null;

        try
        {
            var recordsToPrint = GetRecordsToPrint();
            if (recordsToPrint.Count == 0)
            {
                StatusMessage = "No records match the print range.";
                return;
            }

            // An "Even pages only" run is the second (back-side) pass of a manual duplex
            // job whose cards were already counted and recorded on the first pass.
            var isSecondDuplexPass = PageParity == PageParityFilter.EvenPages;
            var startedUtc = DateTime.UtcNow;

            // Print history (FR-PRN-4): people who already have a card for this design
            // are a reprint -- ask why, or let the operator print only the new people.
            ReprintDecision? reprintDecision = null;
            if (!isSecondDuplexPass)
            {
                var (confirmedRows, decision) = await ConfirmReprintsAsync(recordsToPrint);
                if (confirmedRows is null)
                {
                    StatusMessage = "Printing cancelled.";
                    return;
                }
                if (confirmedRows.Count == 0)
                {
                    StatusMessage = "Nothing to print: everyone in this run already has a card.";
                    return;
                }
                recordsToPrint = confirmedRows;
                reprintDecision = decision;
            }

            // Physical cards this job produces.
            var cardsInJob = isSecondDuplexPass ? 0 : recordsToPrint.Count * NumberOfCopies;

            // Who may print (licensed / trial / log in first) is decided here, in the
            // print routine itself, for every job -- never just by the button's state.
            if (_printAuthorization is not null)
            {
                // Trial cards are counted per card: odd/even page passes would print
                // pages without counting them, so they need an activated license.
                if (PageParity != PageParityFilter.AllPages && _trialState is { IsActivated: false })
                {
                    StatusMessage = "Printing odd or even pages only needs an activated license. Set Pages to All pages.";
                    return;
                }

                authorization = await AuthorizeAsync(cardsInJob);
                if (authorization is { IsAllowed: false })
                {
                    await HandleDenialAsync(authorization);
                    return;
                }

            }
            else
            {
                var licenseProblem = await CheckLicenseAsync(cardsInJob);
                if (licenseProblem is not null)
                {
                    StatusMessage = licenseProblem;
                    return;
                }
            }
            var watermark = authorization?.Watermark == true;
            // Trial: the cards were reserved for exactly NumberOfCopies, so the Windows
            // print dialog may lower the copies but not raise them, and has no page range.
            var copiesAreLimit = authorization is { IsTrial: true };

            var applyRotation = ShouldApplyRotation;

            tempDir = Path.Combine(Path.GetTempPath(), "InfoID_Print_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            var pagePaths = new List<string>();
            var pdfPages = new List<CardPdfWriter.PdfPage>();
            var pageIndex = 0;
            foreach (var row in recordsToPrint)
            {
                var options = BuildRenderOptions(row.Record, trialWatermark: watermark);
                if (PrintFront) foreach (var p in await SavePageAsync(tempDir, CardSide.Front, options, pageIndex++, applyRotation)) { pagePaths.Add(p.Path); pdfPages.Add(p.PdfPage); }
                if (PrintBack) foreach (var p in await SavePageAsync(tempDir, CardSide.Back, options, pageIndex++, applyRotation)) { pagePaths.Add(p.Path); pdfPages.Add(p.PdfPage); }
            }

            if (PageParity != PageParityFilter.AllPages)
            {
                pagePaths = ApplyParity(pagePaths, PageParity);
                pdfPages = ApplyParity(pdfPages, PageParity);
                if (pagePaths.Count == 0)
                {
                    StatusMessage = "There are no even pages in this run.";
                    return;
                }
            }

            var printerName = SelectedPrinter?.Name ?? string.Empty;
            PrintJobResult result;

            if (useSystemDialog)
            {
                StatusMessage = "Choose the printer and settings in the Windows print dialog...";
                result = await _printerService.PrintPagesWithSystemDialogAsync(printerName, pagePaths, NumberOfCopies, copiesAreLimit);
            }
            else
            {
                StatusMessage = $"Sending {pagePaths.Count} page(s) x {NumberOfCopies} {(NumberOfCopies == 1 ? "copy" : "copies")} to {printerName}...";
                var ok = await _printerService.PrintPagesAsync(printerName, pagePaths, NumberOfCopies);
                result = ok ? PrintJobResult.Success(printerName) : PrintJobResult.Failure(null);
            }

            // Closing the Windows print dialog is not a failure: leave every record's
            // print status exactly as it was (the finally block gives trial cards back).
            if (result.Cancelled)
            {
                StatusMessage = string.IsNullOrWhiteSpace(result.Error) ? "Printing cancelled." : result.Error;
                return;
            }

            var success = result.Succeeded;
            // Copies really printed (the Windows dialog can change them) and the cards
            // that makes -- what the license counter / trial count is charged.
            var copiesPrinted = result.CopiesPrinted is { } reported && reported > 0 ? reported : NumberOfCopies;
            if (copiesAreLimit) copiesPrinted = Math.Min(copiesPrinted, NumberOfCopies);
            var cardsPrinted = isSecondDuplexPass ? 0 : recordsToPrint.Count * copiesPrinted;
            // Counts and printer only -- never cardholder data in the log.
            if (success)
                Log.Info($"Print run sent: {recordsToPrint.Count} record(s) x {copiesPrinted} copies, {pagePaths.Count} page(s), " +
                         $"design \"{Document.Name}\", printer \"{result.PrinterName ?? printerName}\", colour mode {ColorMode}, pages {PageParity}.");
            else
                Log.Warn($"Print run failed on printer \"{result.PrinterName ?? printerName}\" for design \"{Document.Name}\": " +
                         (string.IsNullOrWhiteSpace(result.Error) ? "no error text from the printer" : result.Error));
            foreach (var row in recordsToPrint)
            {
                if (success)
                {
                    row.IsPrinted = true;
                    ApplyStatusMappings(row);
                }
                else
                {
                    row.State = PrintRecordState.Failed;
                }
            }
            await PersistStatusesAsync();

            if (success)
            {
                if (authorization is not null && _printAuthorization is not null)
                    await _printAuthorization.CompleteAsync(authorization, cardsPrinted); // license counter or trial cards
                else
                    await CountPrintedCardsAsync(cardsPrinted);
            }

            // Recorded for successful AND failed runs (a failed job shows in history
            // without cards); skipped for the even-pages pass, which isn't a new job.
            string? historyNote = null;
            if (!isSecondDuplexPass)
            {
                historyNote = await RecordPrintHistoryAsync(
                    recordsToPrint, success,
                    string.IsNullOrWhiteSpace(result.Error) ? "The printer reported the job could not be sent." : result.Error,
                    result.PrinterName ?? printerName, startedUtc, reprintDecision, copiesPrinted);
            }

            if (success && DigitalCopyMode != DigitalCopyMode.Off)
            {
                await SaveAutomaticDigitalCopyAsync(pagePaths, pdfPages);
            }

            var trialNote = authorization is { IsTrial: true } && success && _trialState is { HasTrialStatus: true } trial
                ? $" Trial: {trial.TrialUsageText}, {trial.TrialRemainingText}."
                : "";
            StatusMessage = success
                ? $"Sent {recordsToPrint.Count} card(s) to {result.PrinterName ?? printerName}." +
                  (historyNote is null ? "" : " " + historyNote) + trialNote
                : string.IsNullOrWhiteSpace(result.Error)
                    ? "The printer reported the job could not be sent. Check Preferences and try again."
                    : $"Print failed: {result.Error}";
        }
        catch (Exception ex)
        {
            Log.Error("Print failed with an exception", ex);
            StatusMessage = $"Print failed: {ex.Message}";
        }
        finally
        {
            // Cancelled, failed, or an error before the job was sent: the reserved trial
            // cards go back (a completed job was already counted and is left alone).
            if (authorization is { IsAllowed: true, Finished: false } && _printAuthorization is not null)
                await _printAuthorization.CancelAsync(authorization);

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
        var page = RenderPagePixels(side, options, disableAntialiasing, applyRotation, monoChrome);

        using var bitmap = RewrapAsBitmap(page.Bgra, page.Width, page.Height);
        var path = Path.Combine(tempDir, $"page_{fileSuffix}.png");
        await using (var stream = File.Create(path))
        {
            bitmap.Save(stream);
        }

        var pdfPage = new CardPdfWriter.PdfPage(page.Bgra, page.Width, page.Height, Document.WidthMm, Document.HeightMm);
        return (path, pdfPage);
    }

    /// <summary>The single print-output pixel pipeline: render one side at print DPI,
    /// then apply the same MonoChrome threshold / Rotate 180 / antialiasing settings the
    /// printer receives. Used by Print, the automatic digital copy AND the manual
    /// "Digital Copy" button, so a saved copy always matches what would print.</summary>
    private (byte[] Bgra, int Width, int Height) RenderPagePixels(
        CardSide side, ThumbnailRenderer.RenderOptions options,
        bool disableAntialiasing, bool applyRotation, bool monoChrome)
    {
        // Disposed per page: a batch of hundreds of cards would otherwise hold every
        // rendered bitmap in native memory until the GC happened to get to them.
        using var rendered = CardRasterizer.RenderSide(Document, side, _assetService, options, disableAntialiasing: disableAntialiasing);
        var bgra = CardRasterizer.ExtractBgraPixels(rendered);
        var width = rendered.PixelSize.Width;
        var height = rendered.PixelSize.Height;
        var rowBytes = bgra.Length / height;

        FlattenOntoWhite(bgra);

        if (monoChrome)
        {
            ApplyMonoChrome(bgra, (byte)Math.Clamp(BlackPanelThreshold, 0, 255));
        }
        if (applyRotation)
        {
            CardRasterizer.Rotate180InPlace(bgra, width, height, rowBytes);
        }

        return (bgra, width, height);
    }

    /// <summary>Rotate 180 as configured on the Advanced tab, for the current card
    /// orientation.</summary>
    private bool ShouldApplyRotation =>
        Rotate180
        || (Rotate180Landscape && Document.Orientation == CardOrientation.Landscape)
        || (Rotate180Portrait && Document.Orientation == CardOrientation.Portrait);

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

    /// <summary>Composites the rendered card onto white paper. Bug fix: the area
    /// outside the card's rounded corners (and any other transparent pixel) is stored
    /// as premultiplied BGRA (0,0,0,0). Every consumer here treats the image as opaque
    /// (the PNG sent to the printer, the PDF writer, which drops alpha, and the
    /// MonoChrome threshold), so those pixels came out BLACK: the black corner
    /// triangles on printed cards and PDFs, wasting ribbon on a real card printer.
    /// Premultiplied "over white": channel + (255 - alpha), then fully opaque.</summary>
    private static void FlattenOntoWhite(byte[] bgra)
    {
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var alpha = bgra[i + 3];
            if (alpha == 255) continue;

            var inverse = 255 - alpha;
            bgra[i] = (byte)Math.Min(255, bgra[i] + inverse);
            bgra[i + 1] = (byte)Math.Min(255, bgra[i + 1] + inverse);
            bgra[i + 2] = (byte)Math.Min(255, bgra[i + 2] + inverse);
            bgra[i + 3] = 255;
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
    private static WriteableBitmap RewrapAsBitmap(byte[] bgra, RenderTargetBitmap source) =>
        RewrapAsBitmap(bgra, source.PixelSize.Width, source.PixelSize.Height);

    /// <summary>Same as above for a buffer whose source bitmap has already been
    /// disposed. 96 DPI matches CardRasterizer's own identity-scale bitmaps.</summary>
    private static WriteableBitmap RewrapAsBitmap(byte[] bgra, int width, int height)
    {
        var writeable = new WriteableBitmap(new Avalonia.PixelSize(width, height), new Avalonia.Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var fb = writeable.Lock();
        var byteCount = Math.Min(bgra.Length, fb.RowBytes * fb.Size.Height);
        Marshal.Copy(bgra, 0, fb.Address, byteCount);
        return writeable;
    }

    // ---------------------------------------------------------------- preview rendering ----

    /// <summary>Re-renders both the Color and Black previews for the current record.
    /// Called on every record change (Next/Previous, or initial load).</summary>
    private void RefreshAllPreviews()
    {
        RefreshColorPreview();
        RefreshBlackPreview();
    }

    [RelayCommand]
    private void RefreshColorPreview()
    {
        try
        {
            var options = BuildRenderOptions(PreviewRecord);
            _previewWatermark = options.TrialWatermark;

            // Assign the new bitmap first, THEN dispose the old one, so the Image
            // control is never asked to draw an already-disposed bitmap.
            var oldFront = ColorPreviewFront;
            var oldBack = ColorPreviewBack;
            ColorPreviewFront = CardRasterizer.RenderSide(Document, CardSide.Front, _assetService, options, dpi: PreviewDpi);
            ColorPreviewBack = CardRasterizer.RenderSide(Document, CardSide.Back, _assetService, options, dpi: PreviewDpi);
            oldFront?.Dispose();
            oldBack?.Dispose();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't render color preview: {ex.Message}";
        }
    }

    /// <summary>Kept under its original name so any existing
    /// RefreshBlackPreviewCommand binding in the View keeps working.</summary>
    [RelayCommand]
    private void RefreshBlackPreview()
    {
        try
        {
            var options = BuildRenderOptions(PreviewRecord);
            var threshold = (byte)Math.Clamp(BlackPanelThreshold, 0, 255);

            var oldFront = BlackPreviewFront;
            var oldBack = BlackPreviewBack;
            BlackPreviewFront = RenderMonoChromePreview(CardSide.Front, options, threshold);
            BlackPreviewBack = RenderMonoChromePreview(CardSide.Back, options, threshold);
            oldFront?.Dispose();
            oldBack?.Dispose();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't render monochrome preview: {ex.Message}";
        }
    }

    private Bitmap RenderMonoChromePreview(CardSide side, ThumbnailRenderer.RenderOptions options, byte threshold)
    {
        // A lower DPI than actual print output -- this is an on-screen preview, not the
        // file that gets sent to the printer, so there is no reason to pay 300 DPI's
        // render/pixel-processing cost every time the threshold slider moves.
        var rendered = CardRasterizer.RenderSide(Document, side, _assetService, options, dpi: PreviewDpi);
        var bgra = CardRasterizer.ExtractBgraPixels(rendered);
        FlattenOntoWhite(bgra);
        ApplyMonoChrome(bgra, threshold);
        var result = RewrapAsBitmap(bgra, rendered);
        rendered.Dispose();
        return result;
    }

    // Record change -> BOTH previews follow. Threshold change -> only Black is affected.
    partial void OnPreviewRecordChanged(PreviewRecord? value)
    {
        RefreshAllPreviews();
        if (RangeMode == PrintRangeMode.CurrentRecord) OnPropertyChanged(nameof(RangeSummary));
    }
    partial void OnBlackPanelThresholdChanged(double value) => RefreshBlackPreview();

    /// <summary>Manual "Digital Copy" button. Bug fix: this used to always render the
    /// card in full color, ignoring the Black (Color mode) setting, Rotate 180 and
    /// Antialiasing -- so choosing MonoChrome still saved a color PDF. It now goes
    /// through RenderPagePixels, the same pipeline Print uses: MonoChrome saves the
    /// thresholded black page, Composite the color page, Composite + MonoChrome both
    /// (color page then black page for each side, exactly the printer's passes).</summary>
    [RelayCommand]
    private async Task SaveDigitalCopy()
    {
        // With a license a digital copy isn't a card, so it isn't counted -- but it
        // still needs a valid license, or it would be a way around the printing lock.
        // In trial mode it is a card (a PDF can be printed anywhere): it uses one trial
        // card and carries the watermark. Without either, log in first.
        InfoID.Desktop.Features.Trial.Services.PrintAuthorization? authorization = null;
        if (_printAuthorization is null)
        {
            var licenseProblem = await CheckLicenseAsync(0);
            if (licenseProblem is not null)
            {
                StatusMessage = licenseProblem;
                return;
            }
        }

        try
        {
            var suffix = ColorMode switch
            {
                PrintColorMode.MonoChrome => "_Black",
                PrintColorMode.CompositeAndMonoChrome => "_ColorAndBlack",
                _ => string.Empty,
            };

            if (_printAuthorization is not null)
            {
                var isLicensed = _trialState?.IsActivated == true;
                authorization = await AuthorizeAsync(isLicensed ? 0 : 1);
                if (authorization is { IsAllowed: false })
                {
                    await HandleDenialAsync(authorization);
                    return;
                }
            }

            var path = await _filePickerService.PickSaveFileAsync(
                "Save Digital Copy", "PDF", new[] { "*.pdf" }, $"{Document.Name}_DigitalCopy{suffix}", "pdf");
            if (path is null) return;

            var record = PreviewRecord ?? Records.FirstOrDefault()?.Record;
            var options = BuildRenderOptions(record, trialWatermark: authorization?.Watermark == true);
            var disableAntialiasing = AntialiasMode == PrintAntialiasMode.No;
            var applyRotation = ShouldApplyRotation;

            var sides = new List<CardSide>();
            if (PrintFront) sides.Add(CardSide.Front);
            if (PrintBack) sides.Add(CardSide.Back);
            if (sides.Count == 0) sides.Add(CardSide.Front);

            var pages = new List<CardPdfWriter.PdfPage>();
            foreach (var side in sides)
            {
                if (ColorMode is PrintColorMode.Composite or PrintColorMode.CompositeAndMonoChrome)
                    pages.Add(ToPdfPage(RenderPagePixels(side, options, disableAntialiasing, applyRotation, monoChrome: false)));

                if (ColorMode is PrintColorMode.MonoChrome or PrintColorMode.CompositeAndMonoChrome)
                    pages.Add(ToPdfPage(RenderPagePixels(side, options, disableAntialiasing, applyRotation, monoChrome: true)));
            }

            var bytes = CardPdfWriter.Build(pages);
            await File.WriteAllBytesAsync(path, bytes);

            var modeText = ColorMode switch
            {
                PrintColorMode.MonoChrome => "black",
                PrintColorMode.CompositeAndMonoChrome => "color + black",
                _ => "color",
            };
            if (authorization is not null && _printAuthorization is not null)
                await _printAuthorization.CompleteAsync(authorization, authorization.Cards);
            StatusMessage = $"Digital copy ({modeText}) saved to {path}." +
                            (authorization is { IsTrial: true } && _trialState is { HasTrialStatus: true } trial
                                ? $" Trial: {trial.TrialUsageText}, {trial.TrialRemainingText}."
                                : "");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't save digital copy: {ex.Message}";
        }
        finally
        {
            if (authorization is { IsAllowed: true, Finished: false } && _printAuthorization is not null)
                await _printAuthorization.CancelAsync(authorization);
        }
    }

    private CardPdfWriter.PdfPage ToPdfPage((byte[] Bgra, int Width, int Height) page) =>
        new(page.Bgra, page.Width, page.Height, Document.WidthMm, Document.HeightMm);

    [RelayCommand]
    private void Close()
    {
        // Closing with the reprint question open counts as Cancel for that print.
        ReprintPrompt?.CancelCommand.Execute(null);
        LoginPrompt?.CancelCommand.Execute(null);
        if (_trialState is not null) _trialState.Changed -= OnTrialStateChanged;
        RequestClose(true);
    }
}
