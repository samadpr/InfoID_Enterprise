using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Core.Navigation;
using InfoID.Desktop.Features.Backup.Services;
using InfoID.Desktop.Features.CardDesigner.ViewModels;
using InfoID.Desktop.Features.Welcome.ViewModels;
using InfoID.Desktop.ViewModels.Base;
using InfoID.Domain.Common.Enums;
using InfoID.Infrastructure.Backup;
using InfoID.Infrastructure.Persistence;
using Material.Icons;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.Backup.ViewModels;

public sealed record IntervalOption(int Days, string Label);

/// <summary>A backup file in the list.</summary>
public sealed class BackupListItem
{
    public BackupListItem(BackupFileInfo info) => Info = info;
    public BackupFileInfo Info { get; }
    public string Title => Info.CreatedLocal.ToString("dd MMM yyyy, HH:mm");
    public string KindText => Info.Kind switch
    {
        BackupKind.Automatic => "Automatic",
        BackupKind.SafetyBeforeRestore => "Before a restore",
        _ => "Manual",
    };
    public string Detail => $"{KindText} · {BackupViewModel.FormatBytes(Info.SizeBytes)}{(Info.IsEncrypted ? " · password protected" : "")}";
    public MaterialIconKind Icon => Info.IsEncrypted ? MaterialIconKind.ArchiveLockOutline
        : Info.Kind == BackupKind.Automatic ? MaterialIconKind.ArchiveClockOutline
        : Info.Kind == BackupKind.SafetyBeforeRestore ? MaterialIconKind.ArchiveRefreshOutline
        : MaterialIconKind.ArchiveOutline;
    public string ToolTip => Info.FilePath;
}

/// <summary>A row of the history (BackupRestoreLog).</summary>
public sealed record BackupHistoryItem(string When, string What, string FileName, bool Succeeded)
{
    public string StatusText => Succeeded ? "Done" : "Failed";
}

public sealed record SummaryLine(string Label, string Value);

/// <summary>
/// Backup &amp; Restore page (FR-ENT-4): back up now (optionally password protected) or to
/// a chosen file, the automatic schedule and backup folder, the list of backups, and a
/// guided restore (check -> summary -> confirm -> safety backup -> restart).
/// Singleton, so a running backup's progress survives leaving the page.
/// </summary>
public sealed partial class BackupViewModel : ViewModelBase
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<BackupViewModel>();

    private readonly INavigationService _navigation;
    private readonly IServiceScopeFactory _scopes;
    private readonly IDialogService _dialogs;
    private readonly IFilePickerService _filePicker;
    private readonly BackupSettingsStore _settings;
    private readonly AutoBackupScheduler _scheduler;
    private readonly BackupOptions _options;
    private readonly CardDesignerViewModel _designer;
    private bool _loadingSettings;
    private int _checkVersion;

    public BackupViewModel(INavigationService navigation, IServiceScopeFactory scopes, IDialogService dialogs,
        IFilePickerService filePicker, BackupSettingsStore settings, AutoBackupScheduler scheduler, BackupOptions options,
        CardDesignerViewModel designer)
    {
        _navigation = navigation;
        _scopes = scopes;
        _dialogs = dialogs;
        _filePicker = filePicker;
        _settings = settings;
        _scheduler = scheduler;
        _options = options;
        _designer = designer;

        _scheduler.BackupFinished += (_, _) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());
        _settings.Changed += (_, _) => Dispatcher.UIThread.Post(LoadSettings);
        LoadSettings();
    }

    public override void OnNavigatedTo(object? parameter) => _ = RefreshAsync();

    [RelayCommand]
    private void GoHome() => _navigation.NavigateToRoot<WelcomeViewModel>();

    // ------------------------------------------------------------------ status ----

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private string? _progressText;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _statusIsError;

    public bool IsNotBusy => !IsBusy;
    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsNotBusy));

    [ObservableProperty] private string _lastBackupText = "Never";
    [ObservableProperty] private bool _lastBackupNeedsAttention = true;
    [ObservableProperty] private string? _lastFailureText;

    /// <summary>What a backup contains, for the "What's included" line.</summary>
    public string IncludedText =>
        string.Join(" · ", _options.Sources.Select(s => s.Description ?? "Settings").Distinct());

    // ----------------------------------------------------------- backup now ----

    [ObservableProperty] private bool _protectWithPassword;
    [ObservableProperty] private string _backupPassword = string.Empty;
    [ObservableProperty] private string _backupPasswordConfirm = string.Empty;
    [ObservableProperty] private string? _passwordError;

    [RelayCommand]
    private Task BackUpNow() => RunManualBackupAsync(null);

    [RelayCommand]
    private async Task SaveBackupAs()
    {
        var suggested = BackupFileNames.Create(DateTime.Now, BackupKind.Manual);
        var path = await _filePicker.PickSaveFileAsync("Save InfoID backup", "InfoID backup", new[] { BackupFileNames.Pattern },
            suggested, BackupFileNames.Extension.TrimStart('.'));
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!path.EndsWith(BackupFileNames.Extension, StringComparison.OrdinalIgnoreCase)) path += BackupFileNames.Extension;
        await RunManualBackupAsync(path);
    }

    private async Task RunManualBackupAsync(string? destination)
    {
        if (IsBusy) return;
        string? password = null;
        if (ProtectWithPassword)
        {
            if (BackupPassword.Length < 6) { PasswordError = "Use at least 6 characters."; return; }
            if (BackupPassword != BackupPasswordConfirm) { PasswordError = "The passwords don't match."; return; }
            password = BackupPassword;
        }
        PasswordError = null;

        try
        {
            destination ??= BackupFileNames.CreateUniquePath(EnsureFolder(_settings.EffectiveFolder), DateTime.Now, BackupKind.Manual);
        }
        catch (Exception ex)
        {
            ShowStatus("The backup folder can't be used: " + ex.GetBaseException().Message, isError: true);
            return;
        }

        IsBusy = true;
        ShowStatus(null);
        try
        {
            var result = await _scheduler.RunAsync(new BackupRequest { DestinationPath = destination, Kind = BackupKind.Manual, Password = password },
                new Progress<BackupProgress>(p => { ProgressValue = p.Fraction * 100; ProgressText = p.Step; }));
            if (result.Success)
            {
                ShowStatus($"Backup saved: {Path.GetFileName(result.FilePath)} ({FormatBytes(result.SizeBytes)}).");
                BackupPassword = BackupPasswordConfirm = string.Empty;
            }
            else
            {
                ShowStatus("The backup failed: " + result.ErrorMessage, isError: true);
            }
        }
        finally
        {
            IsBusy = false;
            ProgressText = null;
            await RefreshAsync();
        }
    }

    // ------------------------------------------------------------ schedule ----

    public IReadOnlyList<IntervalOption> Intervals { get; } = new[]
    {
        new IntervalOption(1, "Every day"),
        new IntervalOption(3, "Every 3 days"),
        new IntervalOption(7, "Every week"),
        new IntervalOption(14, "Every 2 weeks"),
    };

    [ObservableProperty] private bool _autoBackupEnabled;
    [ObservableProperty] private IntervalOption? _selectedInterval;
    [ObservableProperty] private decimal? _keepCount;
    [ObservableProperty] private string _backupFolder = string.Empty;
    [ObservableProperty] private bool _isDefaultFolder;
    [ObservableProperty] private string? _folderHint;

    partial void OnAutoBackupEnabledChanged(bool value)
    {
        if (_loadingSettings) return;
        _settings.Update(s => s.AutoBackupEnabled = value);
        AuditSetting(value ? "Automatic backups turned on" : "Automatic backups turned off",
            new { AutomaticBackups = !value }, new { AutomaticBackups = value });
    }

    partial void OnSelectedIntervalChanged(IntervalOption? oldValue, IntervalOption? newValue)
    {
        if (_loadingSettings || newValue is null) return;
        _settings.Update(s => s.IntervalDays = newValue.Days);
        AuditSetting($"Automatic backup interval set to {newValue.Label.ToLowerInvariant()}",
            new { IntervalDays = oldValue?.Days }, new { IntervalDays = newValue.Days });
    }

    partial void OnKeepCountChanged(decimal? oldValue, decimal? newValue)
    {
        if (_loadingSettings || newValue is not { } keep) return;
        _settings.Update(s => s.KeepCount = (int)keep);
        AuditSetting($"Automatic backups kept set to {(int)keep}", new { Keep = (int?)oldValue }, new { Keep = (int)keep });
    }

    private static void AuditSetting(string description, object before, object after) =>
        InfoID.Desktop.Core.Services.Audit.Record(InfoID.Domain.Common.Interfaces.AuditModules.Backup, "SettingsChanged", description,
            "BackupSettings", before: before, after: after);

    private void LoadSettings()
    {
        _loadingSettings = true;
        try
        {
            var s = _settings.Current;
            AutoBackupEnabled = s.AutoBackupEnabled;
            SelectedInterval = Intervals.FirstOrDefault(i => i.Days == s.IntervalDays)
                               ?? new IntervalOption(s.IntervalDays, $"Every {s.IntervalDays} days");
            KeepCount = s.KeepCount;
            BackupFolder = _settings.EffectiveFolder;
            IsDefaultFolder = string.IsNullOrWhiteSpace(s.Folder);
            FolderHint = SameDriveAsData(BackupFolder)
                ? "This folder is on the same drive as InfoID's data. To be safe from a disk failure or a lost laptop, choose a USB drive, a network folder or a OneDrive folder."
                : null;

            LastBackupText = s.LastBackupUtc is { } last ? DescribeAge(last.ToLocalTime()) : "Never";
            LastBackupNeedsAttention = s.LastBackupUtc is null || DateTime.UtcNow - s.LastBackupUtc > TimeSpan.FromDays(Math.Max(7, s.IntervalDays * 2));
            LastFailureText = s.LastFailureUtc is { } failed && (s.LastBackupUtc is null || failed > s.LastBackupUtc)
                ? $"The last backup failed ({DescribeAge(failed.ToLocalTime())}): {s.LastFailureMessage}"
                : null;
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    [RelayCommand]
    private async Task ChangeFolder()
    {
        var folder = await _filePicker.PickFolderAsync("Choose where InfoID saves backups");
        if (string.IsNullOrWhiteSpace(folder)) return;
        if (IsInsideDataFolder(folder))
        {
            ShowStatus("Choose a folder outside InfoID's own data folder.", isError: true);
            return;
        }
        try
        {
            // Make sure InfoID can actually write there before relying on it.
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, $".infoid-write-test-{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(probe, "test");
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            ShowStatus("InfoID can't save backups in that folder: " + ex.GetBaseException().Message, isError: true);
            return;
        }
        var previousFolder = _settings.EffectiveFolder;
        _settings.Update(s => s.Folder = folder);
        AuditSetting($"Backup folder changed to {folder}", new { Folder = previousFolder }, new { Folder = folder });
        ShowStatus("Backups will be saved in " + folder);
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task UseDefaultFolder()
    {
        var previousFolder = _settings.EffectiveFolder;
        _settings.Update(s => s.Folder = null);
        AuditSetting("Backup folder reset to the default", new { Folder = previousFolder }, new { Folder = BackupSetup.DefaultBackupFolder });
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenFolder() => OpenInExplorer(EnsureFolder(_settings.EffectiveFolder), select: false);

    // --------------------------------------------------------------- list ----

    public ObservableCollection<BackupListItem> Backups { get; } = new();
    public ObservableCollection<BackupHistoryItem> History { get; } = new();
    [ObservableProperty] private BackupListItem? _selectedBackup;
    [ObservableProperty] private bool _hasBackups;
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private bool _restorePending;

    partial void OnSelectedBackupChanged(BackupListItem? value)
    {
        if (value is not null) _ = CheckBackupAsync(value.Info.FilePath, null);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        LoadSettings();
        var selectedPath = SelectedBackup?.Info.FilePath;
        try
        {
            using var scope = _scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var files = await Task.Run(() => service.ListBackups(_settings.EffectiveFolder));
            var history = await service.GetHistoryAsync(30);
            RestorePending = service.IsRestorePending;

            Backups.Clear();
            foreach (var file in files) Backups.Add(new BackupListItem(file));
            HasBackups = Backups.Count > 0;
            if (selectedPath is not null)
            {
                var again = Backups.FirstOrDefault(b => string.Equals(b.Info.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase));
                // Re-select without re-checking the file.
#pragma warning disable MVVMTK0034
                _selectedBackup = again;
#pragma warning restore MVVMTK0034
                OnPropertyChanged(nameof(SelectedBackup));
            }

            History.Clear();
            foreach (var row in history)
            {
                History.Add(new BackupHistoryItem(
                    DescribeAge((row.CompletedDate ?? row.StartedDate).ToLocalTime()),
                    row.OperationType == BackupOperationType.Restore ? "Restore" : "Backup",
                    string.IsNullOrEmpty(row.FilePath) ? "" : Path.GetFileName(row.FilePath),
                    row.Status == BackupStatus.Success));
            }
            HasHistory = History.Count > 0;
        }
        catch (Exception ex)
        {
            Log.Error("Couldn't load the backup list", ex);
            ShowStatus("Couldn't read the backup folder: " + ex.GetBaseException().Message, isError: true);
        }
    }

    [RelayCommand]
    private void ShowInFolder(BackupListItem? item)
    {
        if ((item ?? SelectedBackup) is { } target) OpenInExplorer(target.Info.FilePath, select: true);
    }

    [RelayCommand]
    private async Task DeleteBackup(BackupListItem? item)
    {
        var target = item ?? SelectedBackup;
        if (target is null || IsBusy) return;
        var confirmed = await _dialogs.ShowDialogAsync<MessageDialogViewModel, bool>(MessageDialogViewModel.Confirmation(
            "Delete backup?", $"Delete the backup from {target.Title}? This can't be undone."));
        if (!confirmed) return;
        try
        {
            File.Delete(target.Info.FilePath);
            Log.Info($"Backup deleted by the user: {target.Info.FilePath}");
            InfoID.Desktop.Core.Services.Audit.Record(InfoID.Domain.Common.Interfaces.AuditModules.Backup, "BackupDeleted",
                $"Backup {target.Info.FileName} deleted", "Backup", before: new { File = target.Info.FilePath, target.Info.SizeBytes, Kind = target.Info.Kind.ToString() });
            if (ReferenceEquals(SelectedBackup, target)) ClearRestore();
        }
        catch (Exception ex)
        {
            ShowStatus("Couldn't delete the backup: " + ex.GetBaseException().Message, isError: true);
        }
        await RefreshAsync();
    }

    // ------------------------------------------------------------- restore ----

    [ObservableProperty] private string? _restoreFilePath;
    [ObservableProperty] private bool _isChecking;
    [ObservableProperty] private bool _restoreNeedsPassword;
    [ObservableProperty] private string _restorePassword = string.Empty;
    [ObservableProperty] private string? _restoreProblem;
    [ObservableProperty] private bool _canRestore;
    [ObservableProperty] private string? _restoreTitle;
    public ObservableCollection<SummaryLine> RestoreSummary { get; } = new();

    public bool HasRestoreFile => RestoreFilePath is not null;
    partial void OnRestoreFilePathChanged(string? value) => OnPropertyChanged(nameof(HasRestoreFile));

    [RelayCommand]
    private async Task ChooseRestoreFile()
    {
        var path = await _filePicker.PickOpenFileAsync("Choose an InfoID backup", "InfoID backup", new[] { BackupFileNames.Pattern });
        if (string.IsNullOrWhiteSpace(path)) return;
#pragma warning disable MVVMTK0034
        _selectedBackup = null;
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(SelectedBackup));
        await CheckBackupAsync(path, null);
    }

    [RelayCommand]
    private Task UnlockBackup() =>
        RestoreFilePath is null ? Task.CompletedTask : CheckBackupAsync(RestoreFilePath, RestorePassword);

    private async Task CheckBackupAsync(string path, string? password)
    {
        var version = ++_checkVersion;
        RestoreFilePath = path;
        RestoreTitle = Path.GetFileName(path);
        RestoreProblem = null;
        CanRestore = false;
        RestoreSummary.Clear();
        if (password is null) RestorePassword = string.Empty;
        IsChecking = true;
        try
        {
            using var scope = _scopes.CreateScope();
            var inspection = await scope.ServiceProvider.GetRequiredService<IBackupService>().InspectAsync(path, password);
            if (version != _checkVersion) return; // another file was chosen meanwhile

            RestoreNeedsPassword = inspection.NeedsPassword;
            RestoreProblem = inspection.NeedsPassword && !inspection.WrongPassword && string.IsNullOrEmpty(password)
                ? null // just ask for the password, no red error yet
                : inspection.Problem;
            CanRestore = inspection.CanRestore;
            if (inspection.Manifest is { } m && inspection.CanRestore)
            {
                RestoreSummary.Add(new SummaryLine("Made", $"{m.CreatedUtc.ToLocalTime():dd MMM yyyy, HH:mm} on {m.MachineName}"));
                RestoreSummary.Add(new SummaryLine("InfoID version", m.AppVersion));
                RestoreSummary.Add(new SummaryLine("Designs", m.Counts.Designs.ToString("N0")));
                RestoreSummary.Add(new SummaryLine("Cardholders", m.Counts.Cardholders.ToString("N0")));
                RestoreSummary.Add(new SummaryLine("Cards issued", m.Counts.Cards.ToString("N0")));
                RestoreSummary.Add(new SummaryLine("Print runs", m.Counts.PrintJobs.ToString("N0")));
                var parts = _options.Sources.Where(s => m.Parts.Contains(s.ArchivePath)).Select(s => s.Description ?? "Settings").Distinct();
                RestoreSummary.Add(new SummaryLine("Includes", string.Join(", ", parts)));
                RestoreNeedsPassword = false;
            }
        }
        catch (Exception ex)
        {
            if (version != _checkVersion) return;
            Log.Error("Checking a backup failed", ex);
            RestoreProblem = "The backup couldn't be checked: " + ex.GetBaseException().Message;
        }
        finally
        {
            if (version == _checkVersion) IsChecking = false;
        }
    }

    [RelayCommand]
    private void ClearRestore()
    {
        _checkVersion++;
        RestoreFilePath = null;
        RestoreTitle = null;
        RestoreProblem = null;
        RestoreNeedsPassword = false;
        RestorePassword = string.Empty;
        CanRestore = false;
        IsChecking = false;
        RestoreSummary.Clear();
    }

    [RelayCommand]
    private async Task StartRestore()
    {
        if (!CanRestore || RestoreFilePath is null || IsBusy) return;
        var path = RestoreFilePath;
        var made = RestoreSummary.FirstOrDefault(l => l.Label == "Made")?.Value ?? "";

        var unsaved = _designer.Tabs.Count(t => t.IsDirty);
        var message =
            $"InfoID's data will be replaced with the backup made {made}.\n\n" +
            "• A safety backup of your current data is made first, so this can be undone.\n" +
            "• This PC's license stays as it is.\n" +
            "• InfoID restarts to finish the restore.";
        if (unsaved > 0)
            message += $"\n\n{unsaved} open design(s) have unsaved changes. They will be lost.";

        var confirmed = await _dialogs.ShowDialogAsync<MessageDialogViewModel, bool>(
            MessageDialogViewModel.Confirmation("Restore this backup?", message, "Restore and restart", "Cancel"));
        if (!confirmed) return;

        IsBusy = true;
        ShowStatus(null);
        try
        {
            using var scope = _scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IBackupService>();
            var result = await service.StageRestoreAsync(path, string.IsNullOrEmpty(RestorePassword) ? null : RestorePassword,
                EnsureFolder(_settings.EffectiveFolder),
                new Progress<BackupProgress>(p => { ProgressValue = p.Fraction * 100; ProgressText = p.Step; }));
            if (!result.Success)
            {
                ShowStatus(result.ErrorMessage ?? "The restore couldn't be prepared.", isError: true);
                return;
            }

            ProgressText = "Restarting InfoID";
            if (!AppRestart.RestartNow())
            {
                RestorePending = true;
                await _dialogs.ShowDialogAsync<MessageDialogViewModel, bool>(new MessageDialogViewModel("Restart InfoID",
                    "The restore is ready, but InfoID couldn't restart by itself. Close InfoID and open it again to finish the restore."));
            }
        }
        finally
        {
            IsBusy = false;
            ProgressText = null;
        }
    }

    [RelayCommand]
    private void RestartToFinishRestore()
    {
        if (!AppRestart.RestartNow())
            ShowStatus("InfoID couldn't restart by itself. Close it and open it again to finish the restore.", isError: true);
    }

    [RelayCommand]
    private async Task CancelPendingRestore()
    {
        using var scope = _scopes.CreateScope();
        scope.ServiceProvider.GetRequiredService<IBackupService>().CancelPendingRestore();
        ShowStatus("The prepared restore was cancelled. Nothing was changed.");
        await RefreshAsync();
    }

    // ------------------------------------------------------------- helpers ----

    private void ShowStatus(string? message, bool isError = false)
    {
        StatusMessage = message;
        StatusIsError = isError;
    }

    private static string EnsureFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static bool IsInsideDataFolder(string folder)
    {
        var data = Path.GetFullPath(DatabaseLocation.GetApplicationRoot()).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        var chosen = Path.GetFullPath(folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return chosen.StartsWith(data, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameDriveAsData(string folder)
    {
        try
        {
            return string.Equals(Path.GetPathRoot(Path.GetFullPath(folder)), Path.GetPathRoot(DatabaseLocation.GetApplicationRoot()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void OpenInExplorer(string path, bool select)
    {
        try
        {
            if (select && OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", path }, UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo(select ? Path.GetDirectoryName(path)! : path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn("Couldn't open the backup folder", ex);
            ShowStatus("Couldn't open the folder: " + ex.GetBaseException().Message, isError: true);
        }
    }

    internal static string DescribeAge(DateTime local)
    {
        var today = DateTime.Today;
        if (local.Date == today) return $"today at {local:HH:mm}";
        if (local.Date == today.AddDays(-1)) return $"yesterday at {local:HH:mm}";
        var days = (today - local.Date).Days;
        return days is > 1 and < 7 ? $"{days} days ago ({local:dd MMM, HH:mm})" : local.ToString("dd MMM yyyy, HH:mm");
    }

    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.#} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0.#} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };
}
