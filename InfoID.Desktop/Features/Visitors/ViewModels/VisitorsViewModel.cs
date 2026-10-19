using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Dtos.VisitorModule;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.CardDesigner.Models;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.CardDesigner.Services;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Visitors.Services;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Visitors.ViewModels;

public sealed record VisitorPeriodOption(VisitorPeriod Period, string Label);

public sealed record VisitorPrintFilterOption(VisitorPrintFilter Filter, string Label);

/// <summary>
/// Visitors page (Visitor Management, badge workflow): visitor registrations imported from
/// a file (Google Form import comes next), with search and filters, Add / Edit / Delete,
/// and Print badges -- which opens the badge design with the chosen visitors and goes
/// straight to the existing Print dialog. "Printed" comes from Print history.
/// </summary>
public sealed partial class VisitorsViewModel : ViewModelBase
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<VisitorsViewModel>();

    private readonly INavigationService _navigation;
    private readonly IServiceScopeFactory _scopes;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _filePicker;
    private readonly VisitorPhotoStore _photos = new();
    private CancellationTokenSource? _searchDelay;
    private int _loadVersion;
    private bool _suppressReload;

    public VisitorsViewModel(INavigationService navigation, IServiceScopeFactory scopes, IDialogService dialogs, IFilePickerService filePicker)
    {
        _navigation = navigation;
        _scopes = scopes;
        _dialogs = dialogs;
        _filePicker = filePicker;
#pragma warning disable MVVMTK0034 // defaults without triggering a load from the constructor
        _selectedPeriod = Periods[1];
        _selectedPrintFilter = PrintFilters[0];
#pragma warning restore MVVMTK0034
    }

    public override void OnNavigatedTo(object? parameter) => _ = ReloadAsync();

    [RelayCommand]
    private void GoHome() => _navigation.NavigateToRoot<WelcomeViewModel>();

    // ---------------------------------------------------------------- filters ----

    public IReadOnlyList<VisitorPeriodOption> Periods { get; } = new[]
    {
        new VisitorPeriodOption(VisitorPeriod.Today, "Today"),
        new VisitorPeriodOption(VisitorPeriod.Upcoming, "Today and upcoming"),
        new VisitorPeriodOption(VisitorPeriod.Last7Days, "Last 7 days"),
        new VisitorPeriodOption(VisitorPeriod.Last30Days, "Last 30 days"),
        new VisitorPeriodOption(VisitorPeriod.All, "All visitors"),
    };

    public IReadOnlyList<VisitorPrintFilterOption> PrintFilters { get; } = new[]
    {
        new VisitorPrintFilterOption(VisitorPrintFilter.All, "Printed and not printed"),
        new VisitorPrintFilterOption(VisitorPrintFilter.NotPrinted, "Not printed"),
        new VisitorPrintFilterOption(VisitorPrintFilter.Printed, "Printed"),
    };

    [ObservableProperty] private VisitorPeriodOption _selectedPeriod;
    [ObservableProperty] private VisitorPrintFilterOption _selectedPrintFilter;
    [ObservableProperty] private string _searchText = string.Empty;

    partial void OnSelectedPeriodChanged(VisitorPeriodOption value) { if (!_suppressReload) _ = ReloadAsync(); }
    partial void OnSelectedPrintFilterChanged(VisitorPrintFilterOption value) { if (!_suppressReload) _ = ReloadAsync(); }

    partial void OnSearchTextChanged(string value)
    {
        _searchDelay?.Cancel();
        var cts = _searchDelay = new CancellationTokenSource();
        _ = Task.Delay(350, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = ReloadAsync());
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = string.Empty;

    // ------------------------------------------------------------------- list ----

    public ObservableCollection<VisitorRowViewModel> Visitors { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(EditVisitorCommand))]
    private VisitorRowViewModel? _selectedVisitor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoading;

    [ObservableProperty] private string? _message;
    [ObservableProperty] private bool _messageIsError;
    [ObservableProperty] private string _countText = string.Empty;
    [ObservableProperty] private string _badgeDesignText = "Badge design: not chosen yet";
    [ObservableProperty] private int _checkedCount;

    public bool HasSelection => SelectedVisitor is not null;
    public bool IsEmpty => !IsLoading && Visitors.Count == 0;
    public bool HasChecked => CheckedCount > 0;

    /// <summary>What Print badges / Delete act on: ticked rows, else the selected row.</summary>
    public string ActionTargetText => CheckedCount > 0
        ? $"{CheckedCount} ticked"
        : SelectedVisitor is not null ? "selected visitor" : "no visitor selected";

    partial void OnCheckedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasChecked));
        OnPropertyChanged(nameof(ActionTargetText));
        OnPropertyChanged(nameof(AllChecked));
    }

    partial void OnSelectedVisitorChanged(VisitorRowViewModel? value) => OnPropertyChanged(nameof(ActionTargetText));

    /// <summary>Header checkbox: tick or untick every listed visitor.</summary>
    public bool? AllChecked
    {
        get => Visitors.Count == 0 || CheckedCount == 0 ? false : CheckedCount == Visitors.Count ? true : null;
        set
        {
            var check = value == true;
            foreach (var row in Visitors) row.IsChecked = check;
        }
    }

    [RelayCommand]
    private Task Refresh() => ReloadAsync();

    private async Task ReloadAsync(IReadOnlyCollection<long>? selectIds = null)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        IsLoading = true;
        try
        {
            var filter = new VisitorFilter
            {
                Period = SelectedPeriod.Period,
                PrintStatus = SelectedPrintFilter.Filter,
                Search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                Today = DateOnly.FromDateTime(DateTime.Today),
            };

            IReadOnlyList<VisitorDto> visitors;
            VisitorSettingsDto settings;
            string? designName = null;
            using (var scope = _scopes.CreateScope())
            {
                var service = scope.ServiceProvider.GetRequiredService<IVisitorService>();
                visitors = await service.GetVisitorsAsync(filter);
                settings = await service.GetSettingsAsync();
                if (settings.BadgeTemplateId is { } templateId)
                {
                    var repository = scope.ServiceProvider.GetRequiredService<ICardDesignRepository>();
                    designName = (await repository.LoadAsync(templateId))?.Name;
                }
            }
            if (version != _loadVersion) return;

            var keepChecked = selectIds is null
                ? Visitors.Where(v => v.IsChecked).Select(v => v.Id).ToHashSet()
                : new HashSet<long>();
            var keepSelected = SelectedVisitor?.Id;

            foreach (var row in Visitors) row.PropertyChanged -= OnRowPropertyChanged;
            Visitors.Clear();
            foreach (var dto in visitors)
            {
                var row = new VisitorRowViewModel(dto, _photos) { IsChecked = keepChecked.Contains(dto.Id) || (selectIds?.Contains(dto.Id) ?? false) };
                row.PropertyChanged += OnRowPropertyChanged;
                Visitors.Add(row);
            }
            UpdateCheckedCount();
            SelectedVisitor = Visitors.FirstOrDefault(v => v.Id == keepSelected) ?? Visitors.FirstOrDefault();

            var notPrinted = visitors.Count(v => !v.IsPrinted);
            CountText = visitors.Count == 0
                ? string.Empty
                : $"{visitors.Count:N0} visitor{(visitors.Count == 1 ? "" : "s")} · {notPrinted:N0} not printed";
            BadgeDesignText = settings.BadgeTemplateId is null
                ? "Badge design: not chosen yet"
                : designName is null ? "Badge design: the chosen design no longer exists" : $"Badge design: {designName}";
            if (!MessageIsError) Message = null;
        }
        catch (Exception ex)
        {
            Log.Error("Visitors couldn't be loaded", ex);
            ShowMessage("Visitors couldn't be loaded: " + ex.GetBaseException().Message, isError: true);
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    private void OnRowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VisitorRowViewModel.IsChecked)) UpdateCheckedCount();
    }

    private void UpdateCheckedCount() => CheckedCount = Visitors.Count(v => v.IsChecked);

    private List<VisitorRowViewModel> ActionTargets() =>
        CheckedCount > 0
            ? Visitors.Where(v => v.IsChecked).ToList()
            : SelectedVisitor is null ? new List<VisitorRowViewModel>() : new List<VisitorRowViewModel> { SelectedVisitor };

    private void ShowMessage(string? text, bool isError = false)
    {
        MessageIsError = isError && text is not null;
        Message = text;
    }

    // ---------------------------------------------------------------- actions ----

    [RelayCommand]
    private async Task ImportFile()
    {
        var dialog = new VisitorImportDialogViewModel(_filePicker, _scopes);
        var result = await _dialogs.ShowDialogAsync<VisitorImportDialogViewModel, VisitorImportResult?>(dialog);
        if (result is null) return;

        ShowMessage(result.Imported == 0
            ? "No visitors were imported" + (result.SkippedDuplicates > 0 ? $" ({result.SkippedDuplicates} already imported)." : ".")
            : $"{result.Imported} visitor{(result.Imported == 1 ? "" : "s")} imported" +
              (result.SkippedDuplicates > 0 ? $", {result.SkippedDuplicates} already imported" : "") +
              ". They're ticked below: click Print badges.");

        // Show the new visitors, ticked and ready to print.
        _suppressReload = true;
        SelectedPeriod = Periods.First(p => p.Period == VisitorPeriod.All);
        SelectedPrintFilter = PrintFilters[0];
        _suppressReload = false;
        foreach (var row in Visitors) row.IsChecked = false;
        await ReloadAsync(result.Created.Select(c => c.Id).ToList());
    }

    [RelayCommand]
    private async Task AddVisitor()
    {
        var dialog = new VisitorEditDialogViewModel(_scopes, _filePicker, existing: null);
        var saved = await _dialogs.ShowDialogAsync<VisitorEditDialogViewModel, VisitorDto?>(dialog);
        if (saved is null) return;
        ShowMessage($"Visitor {saved.VisitorCode} added.");
        await ReloadAsync(new[] { saved.Id });
        SelectedVisitor = Visitors.FirstOrDefault(v => v.Id == saved.Id) ?? SelectedVisitor;
    }

    private bool CanEditVisitor() => SelectedVisitor is not null;

    [RelayCommand(CanExecute = nameof(CanEditVisitor))]
    private async Task EditVisitor()
    {
        if (SelectedVisitor is not { } row) return;
        var dialog = new VisitorEditDialogViewModel(_scopes, _filePicker, row.Dto);
        var saved = await _dialogs.ShowDialogAsync<VisitorEditDialogViewModel, VisitorDto?>(dialog);
        if (saved is null) return;
        ShowMessage($"Visitor {saved.VisitorCode} saved.");
        await ReloadAsync();
    }

    [RelayCommand]
    private async Task DeleteVisitors()
    {
        var targets = ActionTargets();
        if (targets.Count == 0)
        {
            ShowMessage("Tick the visitors to delete, or select one in the list.");
            return;
        }

        var what = targets.Count == 1 ? $"visitor {targets[0].VisitorCode} ({targets[0].FullName})" : $"{targets.Count} visitors";
        var confirmed = await _dialogs.ShowDialogAsync<MessageDialogViewModel, bool>(MessageDialogViewModel.Confirmation(
            "Delete visitors?",
            $"Delete {what} permanently, with their photos? Badges already printed stay in Print history."));
        if (!confirmed) return;

        try
        {
            IReadOnlyList<string> photos;
            using (var scope = _scopes.CreateScope())
                photos = await scope.ServiceProvider.GetRequiredService<IVisitorService>().DeleteAsync(targets.Select(t => t.Id).ToList());
            foreach (var photo in photos) _photos.Delete(photo);
            ShowMessage(targets.Count == 1 ? $"Visitor {targets[0].VisitorCode} deleted." : $"{targets.Count} visitors deleted.");
        }
        catch (Exception ex)
        {
            Log.Error("Visitors couldn't be deleted", ex);
            ShowMessage("Visitors couldn't be deleted: " + ex.GetBaseException().Message, isError: true);
        }
        await ReloadAsync();
    }

    /// <summary>Opens the badge design with the ticked (or selected) visitors and goes
    /// straight to the Print dialog -- one badge per visitor.</summary>
    [RelayCommand]
    private async Task PrintBadges()
    {
        var targets = ActionTargets();
        if (targets.Count == 0)
        {
            ShowMessage("Tick the visitors to print, or select one in the list.");
            return;
        }
        await OpenBadgeDesignAsync(targets.Select(t => t.Id).ToList(), openPrintDialog: true);
    }

    /// <summary>Opens the badge design in the Card Designer with the listed visitors as
    /// sample data, to adjust the design.</summary>
    [RelayCommand]
    private async Task EditBadgeDesign()
    {
        var ids = Visitors.Select(v => v.Id).Take(200).ToList();
        await OpenBadgeDesignAsync(ids, openPrintDialog: false);
    }

    [RelayCommand]
    private async Task ChooseBadgeDesign()
    {
        var choice = await PickDesignAsync(currentId: await GetBadgeTemplateIdAsync());
        if (choice is null) return;
        await SaveBadgeTemplateAsync(choice.TemplateId);
        ShowMessage($"Visitor badges will use \"{choice.Name}\".");
        await ReloadAsync();
    }

    private async Task OpenBadgeDesignAsync(IReadOnlyCollection<long> ids, bool openPrintDialog)
    {
        try
        {
            var templateId = await GetBadgeTemplateIdAsync();
            string? designName = null;
            if (templateId is { } id)
            {
                using var scope = _scopes.CreateScope();
                designName = (await scope.ServiceProvider.GetRequiredService<ICardDesignRepository>().LoadAsync(id))?.Name;
                if (designName is null) templateId = null; // deleted since it was chosen
            }

            if (templateId is null)
            {
                var choice = await PickDesignAsync(currentId: null);
                if (choice is null) return;
                templateId = choice.TemplateId;
                if (choice.Remember) await SaveBadgeTemplateAsync(choice.TemplateId);
            }

            IReadOnlyList<VisitorDto> visitors;
            using (var scope = _scopes.CreateScope())
                visitors = await scope.ServiceProvider.GetRequiredService<IVisitorService>().GetByIdsAsync(ids);

            var table = VisitorBadgeData.BuildTable(visitors, _photos);
            _navigation.NavigateTo<CardDesignerViewModel>(new OpenDesignWithDataRequest(
                templateId.Value, table, DatabaseType.Visitors, VisitorBadgeData.SourceName, VisitorBadgeData.VisitorId, openPrintDialog));
        }
        catch (Exception ex)
        {
            Log.Error("Badge design couldn't be opened", ex);
            ShowMessage("The badge design couldn't be opened: " + ex.GetBaseException().Message, isError: true);
        }
    }

    private async Task<BadgeDesignChoice?> PickDesignAsync(long? currentId)
    {
        IReadOnlyList<InfoID.Desktop.Features.CardDesigner.Models.RecentDesignSummary> designs;
        using (var scope = _scopes.CreateScope())
            designs = await scope.ServiceProvider.GetRequiredService<ICardDesignRepository>().GetRecentAsync(500);
        var thumbnails = InfoID.Desktop.App.Services.GetService<IThumbnailService>();

        if (designs.Count == 0)
        {
            // Nothing to choose from: offer the ready-made badge instead of a dead end.
            var create = await _dialogs.ShowDialogAsync<MessageDialogViewModel, bool>(MessageDialogViewModel.Confirmation(
                "No badge design yet",
                "Create a ready-made visitor badge now? It shows the photo, name, company, person to meet, visit date and a QR code of the visitor ID. You can change it any time with Edit badge design.",
                confirmText: "Create visitor badge", cancelText: "Not now"));
            if (!create) return null;
            var created = await CreateStarterBadgeAsync();
            return created is null ? null : new BadgeDesignChoice(created.Value.Id, created.Value.Name, Remember: true);
        }

        var picker = new BadgeDesignPickerDialogViewModel(designs, currentId, id => thumbnails?.GetThumbnailPath(id));
        return await _dialogs.ShowDialogAsync<BadgeDesignPickerDialogViewModel, BadgeDesignChoice?>(picker);
    }

    /// <summary>Creates the ready-made visitor badge and makes it the badge design.</summary>
    [RelayCommand]
    private async Task CreateStarterBadge()
    {
        var created = await CreateStarterBadgeAsync();
        if (created is null) return;
        ShowMessage($"\"{created.Value.Name}\" was created and will be used for visitor badges. Use Edit badge design to change it.");
        await ReloadAsync();
    }

    private async Task<(long Id, string Name)?> CreateStarterBadgeAsync()
    {
        try
        {
            long id;
            CardDesignDocument document;
            using (var scope = _scopes.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<ICardDesignRepository>();
                var names = (await repository.GetRecentAsync(500)).Select(d => d.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
                var name = VisitorStarterBadge.DefaultName;
                for (var n = 2; names.Contains(name); n++) name = $"{VisitorStarterBadge.DefaultName} {n}";

                document = VisitorStarterBadge.Create(name);
                id = await repository.SaveAsync(document);
            }

            try
            {
                if (InfoID.Desktop.App.Services.GetService<IThumbnailService>() is { } thumbnails)
                    await thumbnails.GenerateThumbnailAsync(document, id);
            }
            catch (Exception ex)
            {
                Log.Warn("Starter badge thumbnail couldn't be created", ex); // the design itself is saved
            }

            await SaveBadgeTemplateAsync(id);
            return (id, document.Name);
        }
        catch (Exception ex)
        {
            Log.Error("Starter badge couldn't be created", ex);
            ShowMessage("The visitor badge couldn't be created: " + ex.GetBaseException().Message, isError: true);
            return null;
        }
    }

    private async Task<long?> GetBadgeTemplateIdAsync()
    {
        using var scope = _scopes.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IVisitorService>().GetSettingsAsync()).BadgeTemplateId;
    }

    private async Task SaveBadgeTemplateAsync(long templateId)
    {
        using var scope = _scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IVisitorService>().SaveSettingsAsync(new VisitorSettingsDto { BadgeTemplateId = templateId });
    }
}
