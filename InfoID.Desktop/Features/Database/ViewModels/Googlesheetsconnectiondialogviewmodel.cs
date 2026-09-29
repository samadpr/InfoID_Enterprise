using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Database.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.Database.ViewModels;

public partial class GoogleSheetsConnectionDialogViewModel
    : DialogViewModelBase<DatabaseConnection?>
{
    private const string UserDataPolicyUrl =
        "https://developers.google.com/terms/api-services-user-data-policy";

    private readonly GoogleSheetsDatabaseService _service = new();
    private CancellationTokenSource? _cts;

    // Set while code (not the user) changes SelectedSpreadsheet and fills tabs itself.
    private bool _suppressTabLoad;

    // A private link pasted before signing in; opened automatically after sign-in.
    private string? _pendingPrivateLink;

    public override string Title => "Connect to database...";

    public ObservableCollection<GoogleSpreadsheetItem> Spreadsheets { get; } = new();

    public ObservableCollection<GoogleSheetTab> SheetTabs { get; } = new();

    [ObservableProperty]
    private string _sheetLink = string.Empty;

    // True when the pasted link was loaded publicly (no sign-in).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTabPickerVisible))]
    private bool _isPublicLinkLoaded;

    [ObservableProperty]
    private string _publicLinkSummary = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedOut))]
    private bool _isSignedIn;

    [ObservableProperty]
    private string _signedInAs = string.Empty;

    [ObservableProperty]
    private GoogleSpreadsheetItem? _selectedSpreadsheet;

    [ObservableProperty]
    private GoogleSheetTab? _selectedSheetTab;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool IsSignedOut => !IsSignedIn;

    public bool IsNotBusy => !IsBusy;

    public bool IsTabPickerVisible => !IsPublicLinkLoaded;

    public GoogleSheetsConnectionDialogViewModel()
    {
        // Restore a previous sign-in silently so returning users go straight to the list.
        _ = RestoreSessionAsync();
    }

    // ---------------------------------------------------------------
    // Paste a link
    // ---------------------------------------------------------------

    partial void OnSheetLinkChanged(string value)
    {
        // Editing the link invalidates a previously loaded public link.
        if (IsPublicLinkLoaded)
        {
            IsPublicLinkLoaded = false;
            PublicLinkSummary = string.Empty;
        }

        _pendingPrivateLink = null;
    }

    [RelayCommand]
    private async Task LoadLink()
    {
        var spreadsheetId = GoogleSheetsDatabaseService.ExtractSpreadsheetId(SheetLink);

        if (spreadsheetId is null)
        {
            StatusMessage = "Paste a Google Sheets link (https://docs.google.com/spreadsheets/d/...).";
            return;
        }

        var gid = GoogleSheetsDatabaseService.ExtractGid(SheetLink);

        await RunAsync(async ct =>
        {
            if (IsSignedIn)
            {
                // Signed in: open through the account, which works for public and private sheets.
                await OpenThroughAccountAsync(spreadsheetId, gid, ct);
                return;
            }

            StatusMessage = "Loading sheet...";

            try
            {
                var data = await _service.GetPublicSheetAsync(spreadsheetId, gid, ct);

                if (data.Columns.Count == 0)
                {
                    StatusMessage = "The sheet is empty.";
                    return;
                }

                ClearAccountSelection();

                IsPublicLinkLoaded = true;
                PublicLinkSummary =
                    $"Public sheet loaded: {data.Rows.Count} rows, {data.Columns.Count} columns" +
                    (gid is null ? " (first tab)." : " (tab from link).");

                StatusMessage = "Click Next to continue.";
            }
            catch (GoogleSheetNotPublicException ex)
            {
                _pendingPrivateLink = SheetLink;
                StatusMessage = ex.Message;
            }
        });
    }

    private async Task OpenThroughAccountAsync(string spreadsheetId, int? gid, CancellationToken ct)
    {
        StatusMessage = "Opening spreadsheet...";

        var (title, tabs) = await _service.GetSpreadsheetInfoAsync(spreadsheetId, ct);

        IsPublicLinkLoaded = false;
        PublicLinkSummary = string.Empty;

        var item = Spreadsheets.FirstOrDefault(s => s.Id == spreadsheetId);

        if (item is null)
        {
            item = new GoogleSpreadsheetItem(spreadsheetId, title);
            Spreadsheets.Insert(0, item);
        }

        _suppressTabLoad = true;

        try
        {
            SelectedSpreadsheet = item;
        }
        finally
        {
            _suppressTabLoad = false;
        }

        FillTabs(tabs, gid);
    }

    // ---------------------------------------------------------------
    // Sign in with Google
    // ---------------------------------------------------------------

    private async Task RestoreSessionAsync()
    {
        bool hasSavedSignIn;

        try
        {
            hasSavedSignIn = await _service.HasSavedSignInAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GOOGLE SESSION RESTORE FAILED: {ex.Message}");
            hasSavedSignIn = false;
        }

        if (hasSavedSignIn)
            await SignIn();
    }

    [RelayCommand]
    private async Task SignIn()
    {
        await RunAsync(async ct =>
        {
            StatusMessage = "Signing in with Google. Complete the sign-in in your browser if it opens...";

            SignedInAs = await _service.SignInAsync(ct);
            IsSignedIn = true;

            StatusMessage = "Loading your spreadsheets...";

            var spreadsheets = await _service.ListSpreadsheetsAsync(ct);

            Spreadsheets.Clear();
            foreach (var item in spreadsheets)
                Spreadsheets.Add(item);

            // A private link was pasted before signing in: open it now.
            var pending = _pendingPrivateLink;
            _pendingPrivateLink = null;

            var pendingId = GoogleSheetsDatabaseService.ExtractSpreadsheetId(pending);

            if (pendingId is not null)
            {
                await OpenThroughAccountAsync(
                    pendingId,
                    GoogleSheetsDatabaseService.ExtractGid(pending),
                    ct);
                return;
            }

            if (!IsPublicLinkLoaded)
                SelectedSpreadsheet = null;

            StatusMessage = Spreadsheets.Count == 0
                ? "No spreadsheets were found in this Google account."
                : $"{Spreadsheets.Count} spreadsheet(s) found. Choose one.";
        });
    }

    [RelayCommand]
    private async Task SignOut()
    {
        await RunAsync(async ct =>
        {
            await _service.SignOutAsync(ct);

            IsSignedIn = false;
            SignedInAs = string.Empty;
            Spreadsheets.Clear();
            ClearAccountSelection();

            StatusMessage = "Signed out.";
        });
    }

    partial void OnSelectedSpreadsheetChanged(GoogleSpreadsheetItem? value)
    {
        if (_suppressTabLoad)
            return;

        SheetTabs.Clear();
        SelectedSheetTab = null;

        if (value is null)
            return;

        // Picking from the list switches away from a loaded public link.
        IsPublicLinkLoaded = false;
        PublicLinkSummary = string.Empty;

        _ = LoadTabsAsync(value);
    }

    private async Task LoadTabsAsync(GoogleSpreadsheetItem spreadsheet)
    {
        await RunAsync(async ct =>
        {
            StatusMessage = $"Loading tabs of \"{spreadsheet.Name}\"...";

            var (_, tabs) = await _service.GetSpreadsheetInfoAsync(spreadsheet.Id, ct);

            // The user may have picked another spreadsheet meanwhile.
            if (!ReferenceEquals(SelectedSpreadsheet, spreadsheet))
                return;

            FillTabs(tabs, preferredGid: null);
        });
    }

    private void FillTabs(IReadOnlyList<GoogleSheetTab> tabs, int? preferredGid)
    {
        SheetTabs.Clear();
        foreach (var tab in tabs)
            SheetTabs.Add(tab);

        SelectedSheetTab =
            SheetTabs.FirstOrDefault(t => preferredGid is not null && t.SheetId == preferredGid) ??
            SheetTabs.FirstOrDefault();

        StatusMessage = SheetTabs.Count == 0
            ? "This spreadsheet has no tabs."
            : $"{SheetTabs.Count} tab(s) found. Choose a tab, then click Next.";
    }

    private void ClearAccountSelection()
    {
        _suppressTabLoad = true;

        try
        {
            SelectedSpreadsheet = null;
        }
        finally
        {
            _suppressTabLoad = false;
        }

        SheetTabs.Clear();
        SelectedSheetTab = null;
    }

    // ---------------------------------------------------------------
    // Navigation
    // ---------------------------------------------------------------

    [RelayCommand]
    private async Task Next()
    {
        // A link was pasted but Load wasn't clicked: load it now.
        if (!IsPublicLinkLoaded &&
            SelectedSpreadsheet is null &&
            !string.IsNullOrWhiteSpace(SheetLink))
        {
            await LoadLink();
        }

        if (IsPublicLinkLoaded)
        {
            var gid = GoogleSheetsDatabaseService.ExtractGid(SheetLink);

            RequestClose(new DatabaseConnection
            {
                Name = "Google Sheet",
                Type = DatabaseType.GoogleSheets,
                ConnectionString = SheetLink.Trim(),
                AuthenticationType = GoogleSheetsDatabaseService.PublicLinkMode,
                SelectedTable = gid is null ? "Sheet1" : $"Sheet_gid_{gid}",
                IsConnected = true
            });

            return;
        }

        if (SelectedSpreadsheet is null)
        {
            // If a pasted link failed, LoadLink already explained why; keep that message.
            if (string.IsNullOrWhiteSpace(SheetLink))
                StatusMessage = "Paste a sheet link, or sign in and choose a spreadsheet.";
            return;
        }

        if (SelectedSheetTab is null)
        {
            StatusMessage = "Choose a sheet tab.";
            return;
        }

        RequestClose(new DatabaseConnection
        {
            Name = SelectedSpreadsheet.Name,
            Type = DatabaseType.GoogleSheets,
            ConnectionString = SelectedSpreadsheet.Id,
            AuthenticationType = GoogleSheetsDatabaseService.GoogleAccountMode,
            SelectedTable = SelectedSheetTab.Title,
            IsConnected = true
        });
    }

    [RelayCommand]
    private void Previous()
    {
        _cts?.Cancel();
        RequestClose(null);
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        RequestClose(null);
    }

    [RelayCommand]
    private void OpenUserDataPolicy()
    {
        try
        {
            Process.Start(new ProcessStartInfo(UserDataPolicyUrl)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open the browser: {ex.Message}";
        }
    }

    private async Task RunAsync(Func<CancellationToken, Task> work)
    {
        _cts?.Cancel();

        // Sign-in waits for the browser; don't let it hang forever.
        var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        _cts = cts;

        IsBusy = true;

        try
        {
            await work(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer operation: stay quiet.
            if (ReferenceEquals(_cts, cts))
                StatusMessage = "Cancelled or timed out.";
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.NotFound)
        {
            StatusMessage = "Spreadsheet not found, or this Google account can't open it.";
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode == HttpStatusCode.Forbidden)
        {
            StatusMessage =
                "Access denied by Google. This account may not have access to the sheet, " +
                "or the Google Sheets / Drive APIs aren't enabled in the Cloud project.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_cts, cts))
                IsBusy = false;
        }
    }
}
