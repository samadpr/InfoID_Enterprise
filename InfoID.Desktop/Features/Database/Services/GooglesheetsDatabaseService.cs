using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Util.Store;
using InfoID.Desktop.Features.Database.Models;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.Database.Services;

/// <summary>A spreadsheet in the signed-in user's Google Drive.</summary>
public sealed record GoogleSpreadsheetItem(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A tab inside a spreadsheet. SheetId is the "gid" seen in sheet links.</summary>
public sealed record GoogleSheetTab(string Title, int? SheetId)
{
    public override string ToString() => Title;
}

/// <summary>Thrown when a pasted link points to a sheet that isn't shared publicly.</summary>
public sealed class GoogleSheetNotPublicException : InvalidOperationException
{
    public GoogleSheetNotPublicException()
        : base("This sheet is private. Sign in with Google to open it, " +
               "or share it as \"Anyone with the link can view\".")
    {
    }
}

/// <summary>
/// Public links (no sign-in, CSV export) and
/// Google sign-in (OAuth, desktop flow), spreadsheet listing (Drive API) and
/// sheet loading (Sheets API) for the Google Sheets data source.
/// The OAuth client ships with InfoID as GoogleOAuthClient.json next to the exe,
/// so end users only click "Sign in with Google".
/// </summary>
public sealed class GoogleSheetsDatabaseService
{
    private static readonly InfoID.Shared.Logging.AppLogger Log = InfoID.Shared.Logging.AppLog.For<GoogleSheetsDatabaseService>();

    public const string PublicLinkMode = "Public link";
    public const string GoogleAccountMode = "Google account";
    public const string ClientFileName = "GoogleOAuthClient.json";

    // Versioned key: tokens saved with an older scope set are not reused.
    private const string UserKey = "infoid-google-user-v2";

    private static readonly string[] Scopes =
    {
        SheetsService.Scope.SpreadsheetsReadonly,
        DriveService.Scope.DriveMetadataReadonly
    };

    private static readonly string TokenFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "InfoID",
        "GoogleSheetsTokens");

    private static readonly SemaphoreSlim SessionLock = new(1, 1);

    private static readonly HttpClient Http = new();

    // One signed-in session for the whole app.
    private static GoogleSession? _session;

    private sealed record GoogleSession(
        UserCredential Credential,
        SheetsService Sheets,
        DriveService Drive,
        string Email);

    public static string ClientFilePath =>
        Path.Combine(AppContext.BaseDirectory, ClientFileName);

    public string? SignedInEmail => _session?.Email;

    public bool IsSignedIn => _session is not null;

    // ---------------------------------------------------------------
    // Link parsing
    // ---------------------------------------------------------------

    public static string? ExtractSpreadsheetId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var match = Regex.Match(input, @"/spreadsheets/d/([a-zA-Z0-9_-]+)");
        if (match.Success)
            return match.Groups[1].Value;

        // Allow pasting the bare spreadsheet id
        var trimmed = input.Trim();
        return Regex.IsMatch(trimmed, @"^[a-zA-Z0-9_-]{25,}$") ? trimmed : null;
    }

    public static int? ExtractGid(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var match = Regex.Match(input, @"[#&?]gid=(\d+)");
        return match.Success && int.TryParse(match.Groups[1].Value, out var gid)
            ? gid
            : null;
    }

    // ---------------------------------------------------------------
    // Sign in / out
    // ---------------------------------------------------------------

    /// <summary>True if a previous sign-in was saved, so it can be restored silently.</summary>
    public async Task<bool> HasSavedSignInAsync()
    {
        if (_session is not null)
            return true;

        var token = await new FileDataStore(TokenFolder, true)
            .GetAsync<TokenResponse>(UserKey);

        return token is not null;
    }

    /// <summary>
    /// Opens the browser for Google consent on first use; afterwards the saved
    /// refresh token is used silently. Returns the account's email.
    /// </summary>
    public async Task<string> SignInAsync(CancellationToken cancellationToken)
    {
        await SessionLock.WaitAsync(cancellationToken);

        try
        {
            if (_session is not null)
                return _session.Email;

            if (!File.Exists(ClientFilePath))
            {
                throw new FileNotFoundException(
                    $"Google sign-in isn't configured: {ClientFileName} was not found next to the InfoID executable.",
                    ClientFilePath);
            }

            ClientSecrets secrets;

            using (var stream = File.OpenRead(ClientFilePath))
            {
                secrets = GoogleClientSecrets.FromStream(stream).Secrets;
            }

            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                Scopes,
                UserKey,
                cancellationToken,
                new FileDataStore(TokenFolder, true));

            var sheets = new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "InfoID"
            });

            var drive = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "InfoID"
            });

            var aboutRequest = drive.About.Get();
            aboutRequest.Fields = "user(emailAddress,displayName)";

            var about = await aboutRequest.ExecuteAsync(cancellationToken);

            var email =
                about.User?.EmailAddress ??
                about.User?.DisplayName ??
                "Google account";

            _session = new GoogleSession(credential, sheets, drive, email);

            return email;
        }
        finally
        {
            SessionLock.Release();
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        await SessionLock.WaitAsync(cancellationToken);

        try
        {
            if (_session is not null)
            {
                try
                {
                    // Best effort: also revoke InfoID's access on Google's side.
                    await _session.Credential.RevokeTokenAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    Log.Warn("Google revoke failed", ex);
                }

                _session.Sheets.Dispose();
                _session.Drive.Dispose();
                _session = null;
            }

            await new FileDataStore(TokenFolder, true)
                .DeleteAsync<TokenResponse>(UserKey);
        }
        finally
        {
            SessionLock.Release();
        }
    }

    private static GoogleSession RequireSession() =>
        _session ?? throw new InvalidOperationException("Sign in with Google first.");

    // ---------------------------------------------------------------
    // Listing
    // ---------------------------------------------------------------

    /// <summary>Spreadsheets in the user's Drive (including shared drives), newest first.</summary>
    public async Task<IReadOnlyList<GoogleSpreadsheetItem>> ListSpreadsheetsAsync(
        CancellationToken cancellationToken)
    {
        var drive = RequireSession().Drive;

        var results = new List<GoogleSpreadsheetItem>();
        string? pageToken = null;

        do
        {
            var request = drive.Files.List();
            request.Q = "mimeType='application/vnd.google-apps.spreadsheet' and trashed=false";
            request.Fields = "nextPageToken, files(id, name)";
            request.OrderBy = "modifiedTime desc";
            request.PageSize = 100;
            request.PageToken = pageToken;
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;

            var page = await request.ExecuteAsync(cancellationToken);

            if (page.Files is not null)
            {
                foreach (var file in page.Files)
                {
                    if (!string.IsNullOrWhiteSpace(file.Id))
                        results.Add(new GoogleSpreadsheetItem(file.Id, file.Name ?? file.Id));
                }
            }

            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken) && results.Count < 1000);

        return results;
    }

    /// <summary>Spreadsheet title and its tabs (with gids).</summary>
    public async Task<(string Title, IReadOnlyList<GoogleSheetTab> Tabs)> GetSpreadsheetInfoAsync(
        string spreadsheetId,
        CancellationToken cancellationToken)
    {
        var request = RequireSession().Sheets.Spreadsheets.Get(spreadsheetId);
        request.Fields = "properties.title,sheets.properties(title,sheetId)";

        var spreadsheet = await request.ExecuteAsync(cancellationToken);

        var tabs = (spreadsheet.Sheets ?? new List<Google.Apis.Sheets.v4.Data.Sheet>())
            .Where(s => !string.IsNullOrWhiteSpace(s.Properties?.Title))
            .Select(s => new GoogleSheetTab(s.Properties!.Title!, s.Properties.SheetId))
            .ToList();

        return (spreadsheet.Properties?.Title ?? "Google Sheet", tabs);
    }

    // ---------------------------------------------------------------
    // Loading data
    // ---------------------------------------------------------------

    /// <summary>
    /// Used by AddDatabase.
    /// Public link: ConnectionString holds the pasted link (spreadsheet id + optional gid).
    /// Google account: ConnectionString holds the spreadsheet id, SelectedTable the tab name.
    /// </summary>
    public async Task<DataTable> LoadAsync(
        DatabaseConnection connection,
        CancellationToken cancellationToken)
    {
        if (connection.AuthenticationType == PublicLinkMode)
        {
            var publicId = ExtractSpreadsheetId(connection.ConnectionString)
                ?? throw new InvalidOperationException("Not a valid Google Sheets link.");

            return await GetPublicSheetAsync(
                publicId,
                ExtractGid(connection.ConnectionString),
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(connection.ConnectionString))
            throw new InvalidOperationException("No spreadsheet selected.");

        await SignInAsync(cancellationToken);

        var tab = connection.SelectedTable;

        if (string.IsNullOrWhiteSpace(tab))
        {
            var info = await GetSpreadsheetInfoAsync(connection.ConnectionString, cancellationToken);

            tab = info.Tabs.FirstOrDefault()?.Title
                ?? throw new InvalidOperationException("The spreadsheet has no tabs.");
        }

        return await GetSheetDataAsync(connection.ConnectionString, tab, cancellationToken);
    }

    /// <summary>
    /// Reads a sheet shared as "Anyone with the link" through its CSV export.
    /// No sign-in needed. Throws GoogleSheetNotPublicException for private sheets.
    /// </summary>
    public async Task<DataTable> GetPublicSheetAsync(
        string spreadsheetId,
        int? gid,
        CancellationToken cancellationToken)
    {
        var url =
            $"https://docs.google.com/spreadsheets/d/{spreadsheetId}/export?format=csv" +
            (gid is null ? string.Empty : $"&gid={gid}");

        using var response = await Http.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("Spreadsheet not found. Check the link.");

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new GoogleSheetNotPublicException();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Google returned {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        // A private sheet redirects to the Google sign-in page (HTML) instead of CSV.
        var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            throw new GoogleSheetNotPublicException();

        var csv = await response.Content.ReadAsStringAsync(cancellationToken);

        return BuildTable(ParseCsv(csv));
    }

    public async Task<DataTable> GetSheetDataAsync(
        string spreadsheetId,
        string sheetTitle,
        CancellationToken cancellationToken)
    {
        // Quote the tab name so names with spaces or symbols work: 'My Sheet'
        var escapedTitle = sheetTitle.Replace("'", "''");
        var range = "'" + escapedTitle + "'";

        var response = await RequireSession().Sheets.Spreadsheets.Values
            .Get(spreadsheetId, range)
            .ExecuteAsync(cancellationToken);

        var rows = (response.Values ?? new List<IList<object>>())
            .Select(r => r.Select(v => v?.ToString() ?? string.Empty).ToList())
            .ToList();

        return BuildTable(rows);
    }

    // First row = headers. Blank/duplicate headers renamed, empty rows and
    // trailing empty columns dropped, numeric "id" column typed as long.
    private static DataTable BuildTable(List<List<string>> rows)
    {
        var table = new DataTable("GoogleSheet");

        var nonEmpty = rows
            .Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v)))
            .ToList();

        if (nonEmpty.Count == 0)
            return table;

        var header = nonEmpty[0];
        var dataRows = nonEmpty.Skip(1).ToList();

        var width = nonEmpty.Max(r => r.Count);
        while (width > 0 &&
               nonEmpty.All(r => r.Count < width || string.IsNullOrWhiteSpace(r[width - 1])))
        {
            width--;
        }

        var names = new List<string>();
        for (var c = 0; c < width; c++)
        {
            var raw = c < header.Count ? header[c].Trim() : string.Empty;
            var name = string.IsNullOrEmpty(raw) ? $"Column{c + 1}" : raw;

            var unique = name;
            var n = 2;
            while (names.Contains(unique, StringComparer.OrdinalIgnoreCase))
                unique = $"{name}_{n++}";

            names.Add(unique);
        }

        for (var c = 0; c < width; c++)
        {
            var isNumericId =
                names[c].Equals("id", StringComparison.OrdinalIgnoreCase) &&
                dataRows.All(r =>
                    c >= r.Count ||
                    string.IsNullOrWhiteSpace(r[c]) ||
                    long.TryParse(r[c].Trim(), out _));

            table.Columns.Add(names[c], isNumericId ? typeof(long) : typeof(string));
        }

        foreach (var source in dataRows)
        {
            var row = table.NewRow();

            for (var c = 0; c < width; c++)
            {
                var column = table.Columns[c];
                var value = c < source.Count ? source[c] : string.Empty;

                if (column.DataType == typeof(long))
                {
                    row[column] = string.IsNullOrWhiteSpace(value)
                        ? DBNull.Value
                        : long.Parse(value.Trim());
                }
                else
                {
                    row[column] = value;
                }
            }

            table.Rows.Add(row);
        }

        table.AcceptChanges();
        return table;
    }

    // RFC 4180 CSV: quoted fields, escaped quotes, commas and newlines inside quotes
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        if (text.Length > 0 && text[0] == '\uFEFF')
            text = text.Substring(1);

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }

                continue;
            }

            switch (ch)
            {
                case '"':
                    inQuotes = true;
                    break;

                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;

                case '\r':
                    break;

                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    break;

                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}
