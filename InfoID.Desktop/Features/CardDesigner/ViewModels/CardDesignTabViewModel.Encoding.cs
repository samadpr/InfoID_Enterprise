using CommunityToolkit.Mvvm.Input;
using InfoID.Application.Dtos.Licensing;
using InfoID.Application.Interfaces;
using InfoID.CardEncoding.Contactless;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.Printing.Services;
using InfoID.Desktop.Features.Rfid.Services;
using InfoID.Desktop.Features.Rfid.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>Card encoding (NFC) for this design tab: the Properties panel's CARD ENCODING
/// section binds to <see cref="NfcEncoding"/>. Created on first use.</summary>
public sealed partial class CardDesignTabViewModel
{
    private NfcEncodingEditorViewModel? _nfcEncoding;

    public NfcEncodingEditorViewModel NfcEncoding => _nfcEncoding ??= CreateNfcEncodingEditor();

    private NfcEncodingEditorViewModel CreateNfcEncodingEditor()
    {
        // Designs saved before encoding existed (or with "Encoding": null) get NFC off.
        Document.Encoding ??= new CardEncodingSettings();
        Document.Encoding.Nfc ??= new NfcEncodingSettings();

        var editor = new NfcEncodingEditorViewModel(
            Document.Encoding.Nfc,
            DatabaseColumns,
            GetNfcPreviewRecord,
            new NfcContentBuilder(_evaluator),
            () =>
            {
                NotifyDocumentChanged();
                MarkDirty();
            });

        // Both live as long as this tab, so the subscription can't leak.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SelectedDatabaseRow) or nameof(DatabaseData) or nameof(SelectedPreviewRecord))
                editor.RefreshPreview();
        };
        return editor;
    }

    /// <summary>The record the panel previews: the selected database row, else the first
    /// row, else the sample preview record.</summary>
    private NfcPreviewRecord GetNfcPreviewRecord()
    {
        var view = DatabaseDataView;
        var row = SelectedDatabaseRow ?? (view is { Count: > 0 } ? view[0] : null);

        if (row is not null && row.Row.RowState is not (DataRowState.Deleted or DataRowState.Detached))
        {
            var values = new Dictionary<string, string>();
            foreach (DataColumn column in row.Row.Table.Columns)
            {
                var cell = row.Row[column];
                values[column.ColumnName] = cell == System.DBNull.Value ? string.Empty : cell?.ToString() ?? string.Empty;
            }

            var index = -1;
            if (view is not null)
            {
                for (var i = 0; i < view.Count; i++)
                {
                    if (ReferenceEquals(view[i].Row, row.Row)) { index = i; break; }
                }
            }
            return new NfcPreviewRecord(index >= 0 ? $"Row {index + 1}" : "Selected row", values, IsSample: false);
        }

        var sample = SelectedPreviewRecord ?? PreviewRecords.FirstOrDefault();
        return sample is null
            ? new NfcPreviewRecord("No data", EmptyRecord, IsSample: true)
            : new NfcPreviewRecord($"Sample: {sample.Label}", sample.Fields, IsSample: true);
    }

    // ---------------------------------------------------------------- writing tags ----

    [RelayCommand]
    private async Task WriteNfcCardsAsync()
    {
        var settings = Document.Encoding?.Nfc;
        if (settings is null || !settings.Enabled)
        {
            await ShowNfcMessageAsync("NFC chip is off", "Turn on \"NFC chip\" under Card Encoding first, then choose what to write.");
            return;
        }

        var services = InfoID.Desktop.App.Services;
        var scopes = services.GetService<IServiceScopeFactory>();
        var writer = services.GetService<NfcCardWriter>();
        var readers = services.GetService<IContactlessReaderService>();
        if (scopes is null || writer is null || readers is null)
        {
            await ShowNfcMessageAsync("NFC writing isn't available", "The card encoding services aren't registered.", isError: true);
            return;
        }

        // Licensing is decided in one place (ICardEncodingAccess).
        FeatureCheckResult access;
        using (var scope = scopes.CreateScope())
            access = await scope.ServiceProvider.GetRequiredService<ICardEncodingAccess>().CheckNfcWriteAsync();
        if (!access.IsAllowed)
        {
            await ShowNfcMessageAsync("Writing NFC tags needs a license", access.Reason ?? "");
            return;
        }

        // Rows typed into the Internal Database grid are saved first.
        SaveDatabaseRows(includeCurrentRow: true);

        var columns = DatabaseData?.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToList() ?? new List<string>();
        var job = new NfcWriteJob
        {
            DesignName = string.IsNullOrWhiteSpace(Document.Name) ? "Untitled design" : Document.Name,
            TemplateId = Document.PersistedTemplateId,
            HistoryDesignKey = PrintHistoryPlanner.BuildDesignKey(Document.PersistedTemplateId, Document.SourceFilePath, Document.Name),
            Settings = settings,
            Records = BuildNfcWriteRecords(),
            IsDataBound = DatabaseData is not null,
            Columns = columns,
            UidColumns = GetNfcUidColumns(),
            SaveUidToData = SaveNfcUidToData,
            FindUidInData = FindNfcUidInData,
            RememberUidOption = RememberNfcUidOption,
        };

        var dialog = new NfcWriteDialogViewModel(job, writer, readers, new NfcContentBuilder(_evaluator), scopes);
        await _dialogService.ShowDialogAsync<NfcWriteDialogViewModel, bool>(dialog);
    }

    /// <summary>Ticked rows, else the visible rows -- same selection as Print. A design with
    /// no data writes one tag with its fixed content.</summary>
    private IReadOnlyList<NfcWriteRecord> BuildNfcWriteRecords()
    {
        if (DatabaseData is null)
            return new[] { new NfcWriteRecord(string.IsNullOrWhiteSpace(Document.Name) ? "Card" : Document.Name, new Dictionary<string, string>(), null) };

        var visible = GetVisibleDatabaseRows().Select(v => v.Row).ToList();
        var ticked = visible.Where(_checkedDatabaseRows.Contains).ToList();
        var rows = ticked.Count > 0 ? ticked : visible;
        var table = string.IsNullOrWhiteSpace(_activeDatabaseTableName) ? "Data" : _activeDatabaseTableName;

        return rows.Select(row => new NfcWriteRecord($"{table} · row {DatabaseData.Rows.IndexOf(row) + 1}", RowToRecord(row), row)).ToList();
    }

    /// <summary>Text columns the UID can be saved into (InfoID Internal Database only).</summary>
    private IReadOnlyList<string> GetNfcUidColumns() =>
        IsDatabaseEditable && DatabaseData is not null
            ? DatabaseData.Columns.Cast<DataColumn>()
                .Where(c => c.DataType == typeof(string) && !c.AutoIncrement && !c.ReadOnly)
                .Select(c => c.ColumnName).ToList()
            : new List<string>();

    private string? SaveNfcUidToData(NfcWriteRecord record, string column, string uid)
    {
        if (!IsDatabaseEditable || DatabaseData is null || !DatabaseData.Columns.Contains(column)) return "this data source can't be changed";
        if (record.Row is not { } row || row.Table != DatabaseData || row.RowState is DataRowState.Deleted or DataRowState.Detached)
            return "the record is no longer in the grid";

        row[column] = uid;
        return SaveDatabaseRows(includeCurrentRow: true) ? null : "the record couldn't be saved";
    }

    private string? FindNfcUidInData(NfcWriteRecord record, string column, string uid)
    {
        if (DatabaseData is null || !DatabaseData.Columns.Contains(column)) return null;
        foreach (DataRow row in DatabaseData.Rows)
        {
            if (ReferenceEquals(row, record.Row) || row.RowState is DataRowState.Deleted or DataRowState.Detached) continue;
            if (string.Equals(Convert.ToString(row[column])?.Trim(), uid, StringComparison.OrdinalIgnoreCase))
                return $"Row {DatabaseData.Rows.IndexOf(row) + 1}";
        }
        return null;
    }

    private void RememberNfcUidOption(bool save, string? column)
    {
        var nfc = Document.Encoding.Nfc;
        if (nfc.SaveUidToData == save && nfc.UidColumn == column) return;
        nfc.SaveUidToData = save;
        nfc.UidColumn = column;
        MarkDirty(); // use the same change callback CreateNfcEncodingEditor() passes to NfcEncodingEditorViewModel
    }

    private async Task ShowNfcMessageAsync(string title, string message, bool isError = false) =>
        await _dialogService.ShowDialogAsync<MessageDialogViewModel, bool>(new MessageDialogViewModel(title, message, isError));
}
