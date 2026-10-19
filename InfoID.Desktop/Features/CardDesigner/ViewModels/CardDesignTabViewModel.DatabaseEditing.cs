using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Database.Services;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>
/// Editing the connected table in the designer's data grid.
///
/// Internal Database: every change is written back automatically, like a professional
/// data editor -- new rows are inserted, edited rows updated (matched on the primary
/// key) and deleted rows deleted, without a Save button. A row is saved when it is
/// finished: when you move to another row, click Add row, click outside the grid,
/// switch or disconnect the table, print, preview, save or close the design, and when
/// InfoID closes. A row that can't be saved stays marked with the reason; nothing typed
/// is thrown away silently.
///
/// Change tracking is ADO.NET's own RowState (Added / Modified), which survives the
/// table copies the grid needs to refresh (search, delete, image pick).
///
/// Every other source (Excel, CSV, SQL Server, Google Sheets, the sample database, ...)
/// is read-only here: rows added or changed in the grid are used for this session
/// (preview and printing) and never written anywhere.
/// </summary>
public sealed partial class CardDesignTabViewModel
{
    /// <summary>Type of the connected source; null when nothing is connected.</summary>
    private DatabaseType? _activeDatabaseType;

    private bool _savingDatabaseChanges;
    private bool _databaseAutoSaveQueued;
    private bool _queuedSaveIncludesCurrentRow;

    /// <summary>While another table is being connected, the old grid's rows must not
    /// be written using the new connection's settings.</summary>
    private bool _connectingDatabase;
    private DataTable? _trackedDatabaseTable;
    private string? _trackedDatabaseKey;

    /// <summary>True when grid edits are saved to the source: the InfoID Internal Database.</summary>
    public bool IsDatabaseEditable =>
        _activeDatabaseType == DatabaseType.InternalDatabase &&
        DatabaseData is not null &&
        !string.IsNullOrWhiteSpace(_activeDatabaseTableName);

    /// <summary>Connected to a source whose grid edits are NOT saved anywhere.</summary>
    public bool IsDatabaseSessionOnly => DatabaseData is not null && !IsDatabaseEditable;

    /// <summary>Rows added or changed in the grid that aren't in the database yet
    /// (empty new rows don't count).</summary>
    [ObservableProperty]
    private int _databaseUnsavedCount;

    /// <summary>Rows whose last save failed.</summary>
    [ObservableProperty]
    private int _databaseFailedCount;

    [ObservableProperty]
    private string _databaseSaveStatusText = string.Empty;

    public bool HasDatabaseUnsavedChanges => DatabaseUnsavedCount > 0;

    /// <summary>Tooltip of the info icon shown for read-only sources.</summary>
    public string DatabaseReadOnlyInfo => _activeDatabaseType == DatabaseType.Visitors
        ? "Visitors: these rows come from the Visitors page. Changes here are used for this session only " +
          "(card preview and printing); edit visitors on the Visitors page."
        : "Read-only source: rows added or changed here are used for this session only " +
          "(card preview and printing). Nothing is written back to the source.";
    public bool DatabaseSaveHasError => DatabaseFailedCount > 0;

    partial void OnDatabaseUnsavedCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasDatabaseUnsavedChanges));
        SaveDatabaseChangesCommand.NotifyCanExecuteChanged();
    }

    partial void OnDatabaseFailedCountChanged(int value) => OnPropertyChanged(nameof(DatabaseSaveHasError));

    public string DeleteDatabaseRowsToolTip => IsDatabaseEditable
        ? "Delete the selected rows from the database (ticked rows first, else Ctrl/Shift+click selection, else the current row)"
        : "Remove the selected rows from the grid for this session (the source isn't changed)";

    /// <summary>Raised after rows were saved (new keys, cleared marks) so the grid can
    /// refresh the cells it shows without rebuilding -- a rebuild would interrupt typing.</summary>
    public event EventHandler? DatabaseRowsSaved;

    // ------------------------------------------------------------- cell edits ----

    /// <summary>Writes what was typed into a cell, converted to the column's type.
    /// Returns false with a message when it doesn't fit (the row keeps its old value).</summary>
    public bool TrySetDatabaseCell(DataRowView rowView, string columnName, string? text, out string? error)
    {
        error = null;
        var row = rowView.Row;
        if (row.RowState is DataRowState.Detached or DataRowState.Deleted ||
            !row.Table.Columns.Contains(columnName))
            return true;

        var column = row.Table.Columns[columnName]!;
        if (column.AutoIncrement && IsDatabaseEditable)
            return true; // keys come from the database

        if (!DatabaseCellValue.TryParse(column, text, out var value, out error))
            return false;

        var current = row[column];
        if (Equals(current, value) ||
            (current == DBNull.Value && value == DBNull.Value))
            return true; // unchanged: don't mark the row as edited

        try
        {
            column.ReadOnly = false;
            row[column] = value;
            row.RowError = string.Empty;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        RefreshDatabaseSaveState();
        return true;
    }

    /// <summary>A typed value didn't fit its field: say so on the status line (the cell
    /// itself shows it too while it's open).</summary>
    public void ReportDatabaseCellError(string columnName, string? error)
    {
        DatabaseSaveStatusText = $"'{columnName}' wasn't changed: {error}";
        _databaseStatusShowsPending = true; // the next successful save says "All changes saved"
    }

    /// <summary>Display text for a cell. Unsaved new rows show "New" in the key column
    /// until the database gives them their number.</summary>
    public string GetDatabaseCellText(DataRowView? rowView, string columnName)
    {
        if (rowView is null) return string.Empty;
        var row = rowView.Row;
        if (row.RowState is DataRowState.Detached or DataRowState.Deleted ||
            !row.Table.Columns.Contains(columnName))
            return string.Empty;

        var column = row.Table.Columns[columnName]!;
        if (column.AutoIncrement && IsDatabaseEditable && row.RowState == DataRowState.Added)
            return "New";

        return DatabaseCellValue.Format(row[column]);
    }

    /// <summary>The key column the grid shows read-only (Internal Database only).</summary>
    public bool IsDatabaseKeyColumn(DataColumn column) => IsDatabaseEditable && column.AutoIncrement;

    /// <summary>Row marker for the grid: null (saved), "unsaved" or "error".</summary>
    public string? GetDatabaseRowMark(DataRow row, out string? toolTip)
    {
        toolTip = null;
        if (!IsDatabaseEditable || row.RowState is DataRowState.Detached or DataRowState.Deleted)
            return null;

        if (!string.IsNullOrEmpty(row.RowError))
        {
            toolTip = "Not saved: " + row.RowError;
            return "error";
        }

        if (row.RowState == DataRowState.Modified ||
            (row.RowState == DataRowState.Added && RowHasData(row)))
        {
            toolTip = "Not saved yet: saved when you move to another row";
            return "unsaved";
        }

        return null;
    }

    // ---------------------------------------------------------------- saving ----

    private bool CanSaveDatabaseChanges() => IsDatabaseEditable && DatabaseUnsavedCount > 0;

    [RelayCommand(CanExecute = nameof(CanSaveDatabaseChanges))]
    private void SaveDatabaseChanges() => SaveDatabaseRows(includeCurrentRow: true);

    /// <summary>Called by the grid when focus leaves it, and after the current row
    /// changes: saves on the next dispatcher pass, so the cell's last keystroke and the
    /// grid's own edit-commit have run first.</summary>
    public void QueueDatabaseAutoSave(bool includeCurrentRow)
    {
        if (!IsDatabaseEditable) return;
        _queuedSaveIncludesCurrentRow |= includeCurrentRow;
        if (_databaseAutoSaveQueued) return;
        _databaseAutoSaveQueued = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var include = _queuedSaveIncludesCurrentRow;
            _databaseAutoSaveQueued = false;
            _queuedSaveIncludesCurrentRow = false;
            SaveDatabaseRows(include);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>Moving to another row finishes the previous one: save it. (Selection
    /// briefly going to null while the grid refreshes is not "leaving" a row.)</summary>
    partial void OnSelectedDatabaseRowChanged(DataRowView? oldValue, DataRowView? newValue)
    {
        if (oldValue is null || newValue is null || ReferenceEquals(oldValue.Row, newValue.Row)) return;
        if (oldValue.Row.RowState is DataRowState.Added or DataRowState.Modified)
            QueueDatabaseAutoSave(includeCurrentRow: false);
    }

    /// <summary>Writes every added/changed row of the Internal Database table back.
    /// Returns true when nothing is left unsaved. Safe to call any time (no-op for
    /// read-only sources). Never throws.</summary>
    public bool SaveDatabaseRows(bool includeCurrentRow = true)
    {
        if (!IsDatabaseEditable || DatabaseData is null || _connectingDatabase) return true;
        if (_savingDatabaseChanges) return false;

        _savingDatabaseChanges = true;
        var saved = 0;
        var failed = 0;
        string? firstError = null;

        try
        {
            var table = DatabaseData;
            var tableName = _activeDatabaseTableName;
            var current = SelectedDatabaseRow?.Row;
            var service = new InternalSqliteDatabaseService();
            var keyColumn = table.Columns.Cast<DataColumn>().FirstOrDefault(c => c.AutoIncrement);

            foreach (var row in table.Rows.Cast<DataRow>().ToList())
            {
                if (row.RowState is not (DataRowState.Added or DataRowState.Modified)) continue;
                if (!includeCurrentRow && ReferenceEquals(row, current)) continue;

                // The grid may still hold the row in an edit (IEditableObject).
                if (row.HasVersion(DataRowVersion.Proposed)) row.EndEdit();

                if (row.RowState == DataRowState.Added && !RowHasData(row)) continue; // empty new row

                try
                {
                    if (row.RowState == DataRowState.Added)
                    {
                        var newId = service.InsertRow(tableName, row);
                        if (keyColumn is not null)
                        {
                            keyColumn.ReadOnly = false;
                            row[keyColumn] = Convert.ChangeType(newId, keyColumn.DataType, System.Globalization.CultureInfo.InvariantCulture);
                        }
                    }
                    else if (!service.UpdateRow(tableName, row))
                    {
                        throw new InvalidOperationException(
                            "This row isn't in the database any more (it may have been deleted elsewhere).");
                    }

                    row.RowError = string.Empty;
                    row.AcceptChanges();
                    saved++;
                }
                catch (Exception ex)
                {
                    failed++;
                    row.RowError = FriendlyDatabaseError(ex);
                    firstError ??= row.RowError;
                    Log.Warn($"Couldn't save a row of Internal Database table '{tableName}'", ex);
                }
            }
        }
        catch (Exception ex)
        {
            failed++;
            firstError ??= FriendlyDatabaseError(ex);
            Log.Error("Saving data-grid rows failed", ex);
        }
        finally
        {
            _savingDatabaseChanges = false;
        }

        RefreshDatabaseSaveState(saved > 0 && failed == 0
            ? $"Saved {saved} row{(saved == 1 ? "" : "s")}"
            : null, firstError);

        if (saved > 0 || failed > 0)
        {
            DatabaseRowsSaved?.Invoke(this, EventArgs.Empty);
            if (saved > 0) NotifyDocumentChanged(); // keys changed: card preview / print keys
        }

        return DatabaseUnsavedCount == 0;
    }

    private static string FriendlyDatabaseError(Exception ex)
    {
        var message = ex.GetBaseException().Message;
        if (message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
            return "Another row already has this value in a field that must be unique.";
        if (message.Contains("NOT NULL constraint failed", StringComparison.OrdinalIgnoreCase))
            return "A required field is empty.";
        if (message.Contains("database is locked", StringComparison.OrdinalIgnoreCase))
            return "The database is busy. It will be tried again when you leave the row.";
        if (message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            return "The table doesn't exist any more.";
        return message;
    }

    /// <summary>Recounts unsaved/failed rows and sets the status line.</summary>
    private void RefreshDatabaseSaveState(string? savedMessage = null, string? error = null)
    {
        if (!IsDatabaseEditable || DatabaseData is null)
        {
            DatabaseUnsavedCount = 0;
            DatabaseFailedCount = 0;
            DatabaseSaveStatusText = string.Empty; // read-only sources show an info icon instead
            return;
        }

        var unsaved = 0;
        var failed = 0;
        string? firstRowError = null;
        foreach (DataRow row in DatabaseData.Rows)
        {
            if (row.RowState == DataRowState.Modified ||
                (row.RowState == DataRowState.Added && RowHasData(row)))
            {
                unsaved++;
                if (!string.IsNullOrEmpty(row.RowError))
                {
                    failed++;
                    firstRowError ??= row.RowError;
                }
            }
        }

        DatabaseUnsavedCount = unsaved;
        DatabaseFailedCount = failed;

        var hadPendingMessage = _databaseStatusShowsPending;
        _databaseStatusShowsPending = unsaved > 0;

        if (failed > 0)
            DatabaseSaveStatusText = $"{failed} row{(failed == 1 ? "" : "s")} not saved: {error ?? firstRowError}";
        else if (unsaved > 0)
            DatabaseSaveStatusText = $"{unsaved} unsaved row{(unsaved == 1 ? "" : "s")}";
        else if (savedMessage is not null)
            DatabaseSaveStatusText = savedMessage;
        else if (hadPendingMessage)
            DatabaseSaveStatusText = "All changes saved";
        // else: keep the last "Saved 2 rows" / "Deleted 1 row" (or nothing after connecting)
    }

    /// <summary>The status line currently reports unsaved or failed rows.</summary>
    private bool _databaseStatusShowsPending;

    /// <summary>DatabaseData was replaced (connect, search, delete, add row...): follow
    /// the new table's edits for the unsaved counter.</summary>
    private void OnDatabaseTableReplaced()
    {
        if (_trackedDatabaseTable is not null)
        {
            _trackedDatabaseTable.RowChanged -= OnTrackedDatabaseRowChanged;
            _trackedDatabaseTable.ColumnChanged -= OnTrackedDatabaseColumnChanged;
        }

        // A different table (not a refreshed copy of the same one): fresh status line.
        var key = DatabaseData is null ? null : $"{_activeDatabaseType}:{_activeDatabaseTableName}";
        if (!string.Equals(key, _trackedDatabaseKey, StringComparison.Ordinal))
        {
            DatabaseSaveStatusText = string.Empty;
            _databaseStatusShowsPending = false;
            _trackedDatabaseKey = key;
        }

        _trackedDatabaseTable = DatabaseData;

        if (_trackedDatabaseTable is not null)
        {
            _trackedDatabaseTable.RowChanged += OnTrackedDatabaseRowChanged;
            _trackedDatabaseTable.ColumnChanged += OnTrackedDatabaseColumnChanged;
        }

        OnPropertyChanged(nameof(IsDatabaseEditable));
        OnPropertyChanged(nameof(IsDatabaseSessionOnly));
        OnPropertyChanged(nameof(DeleteDatabaseRowsToolTip));
        OnPropertyChanged(nameof(DatabaseReadOnlyInfo));
        RefreshDatabaseSaveState();
    }

    private void OnTrackedDatabaseRowChanged(object? sender, DataRowChangeEventArgs e)
    {
        if (!_savingDatabaseChanges) RefreshDatabaseSaveState();
    }

    private void OnTrackedDatabaseColumnChanged(object? sender, DataColumnChangeEventArgs e)
    {
        if (!_savingDatabaseChanges) RefreshDatabaseSaveState();
    }

    /// <summary>Before the table goes away (disconnect, another source, closing the
    /// design): saves what's pending, and if some rows can't be saved asks whether to
    /// continue and lose them.</summary>
    public async Task<bool> ConfirmLeaveDatabaseAsync(string action)
    {
        if (!IsDatabaseEditable) return true;
        if (SaveDatabaseRows(includeCurrentRow: true)) return true;

        var count = DatabaseUnsavedCount;
        var dialog = MessageDialogViewModel.Confirmation(
            "Unsaved rows",
            $"{count} row{(count == 1 ? "" : "s")} couldn't be saved to the table '{_activeDatabaseTableName}':\n\n" +
            $"{DatabaseSaveStatusText}\n\n{action} anyway and lose {(count == 1 ? "it" : "them")}?",
            confirmText: action,
            cancelText: "Keep editing");
        return await _dialogService.ShowDialogAsync<MessageDialogViewModel, bool>(dialog);
    }

    // ---------------------------------------------------------------- deleting ----

    /// <summary>Internal Database: deletes the rows from the table (after asking).
    /// Other sources: removes them from the grid for this session only.
    /// Which rows: ticked rows, else Ctrl/Shift-selected rows, else the current row.</summary>
    [RelayCommand]
    private async Task DeleteDatabaseRows(IList? selectedItems)
    {
        if (DatabaseData is null) return;

        var rowsToDelete = _checkedDatabaseRows.Where(r => r.Table == DatabaseData).ToList();

        if (rowsToDelete.Count == 0)
            rowsToDelete = selectedItems?.OfType<DataRowView>().Select(v => v.Row).ToList()
                           ?? new List<DataRow>();

        if (rowsToDelete.Count == 0 && SelectedDatabaseRow is not null)
            rowsToDelete.Add(SelectedDatabaseRow.Row);

        rowsToDelete = rowsToDelete
            .Where(r => r.Table == DatabaseData && r.RowState is not (DataRowState.Detached or DataRowState.Deleted))
            .Distinct()
            .ToList();

        if (rowsToDelete.Count == 0) return;

        if (IsDatabaseEditable)
        {
            // Rows never saved only need to leave the grid.
            var savedRows = rowsToDelete.Where(r => r.RowState != DataRowState.Added).ToList();
            if (savedRows.Count > 0)
            {
                var service = new InternalSqliteDatabaseService();
                string? keyColumnName;
                try
                {
                    keyColumnName = service.GetPrimaryKeyColumn(_activeDatabaseTableName);
                }
                catch (Exception ex)
                {
                    Log.Error("Reading the table's key failed", ex);
                    keyColumnName = null;
                }

                if (keyColumnName is null || !DatabaseData.Columns.Contains(keyColumnName))
                {
                    await _dialogService.ShowDialogAsync<MessageDialogViewModel, bool>(new MessageDialogViewModel(
                        "Can't delete rows",
                        $"The table '{_activeDatabaseTableName}' has no single primary key, so InfoID can't tell its rows apart to delete them.",
                        isError: true));
                    return;
                }

                var n = savedRows.Count;
                var confirm = MessageDialogViewModel.Confirmation(
                    "Delete rows",
                    $"Delete {n} row{(n == 1 ? "" : "s")} from the table '{_activeDatabaseTableName}'?\n\n" +
                    "This can't be undone. Print history already recorded for these people is kept.",
                    confirmText: n == 1 ? "Delete row" : $"Delete {n} rows");
                if (!await _dialogService.ShowDialogAsync<MessageDialogViewModel, bool>(confirm))
                    return;

                var keyColumn = DatabaseData.Columns[keyColumnName]!;
                var keys = savedRows
                    .Select(r => r.HasVersion(DataRowVersion.Original) ? r[keyColumn, DataRowVersion.Original] : r[keyColumn])
                    .Where(k => k != DBNull.Value)
                    .ToList();

                try
                {
                    service.DeleteRows(_activeDatabaseTableName, keys);
                }
                catch (Exception ex)
                {
                    Log.Error("Deleting Internal Database rows failed", ex);
                    await _dialogService.ShowDialogAsync<MessageDialogViewModel, bool>(new MessageDialogViewModel(
                        "Rows weren't deleted",
                        "Nothing was deleted: " + FriendlyDatabaseError(ex),
                        isError: true));
                    return;
                }
            }
        }

        var firstIndex = GetVisibleDatabaseRows().FindIndex(r => rowsToDelete.Contains(r.Row));

        // Clear selection first: the selection handler reads the row's values,
        // which fails once the row has been removed.
        SelectedDatabaseRow = null;

        foreach (var row in rowsToDelete)
        {
            _checkedDatabaseRows.Remove(row);
            if (row.Table == DatabaseData) DatabaseData.Rows.Remove(row);
        }

        ReplaceDatabaseTable(DatabaseData, DatabaseData.DefaultView.RowFilter);

        var remaining = GetVisibleDatabaseRows();
        if (remaining.Count > 0)
            SelectedDatabaseRow = remaining[Math.Clamp(firstIndex, 0, remaining.Count - 1)];

        if (IsDatabaseEditable)
        {
            var n = rowsToDelete.Count;
            RefreshDatabaseSaveState($"Deleted {n} row{(n == 1 ? "" : "s")}");
        }

        NotifyDocumentChanged();
    }

    // ---------------------------------------------------------------- adding ----

    /// <summary>Saves what's pending (that's how the previous row gets saved), then adds
    /// an empty row at the end and makes it current.</summary>
    [RelayCommand]
    private async Task AddDatabaseRow()
    {
        if (DatabaseData is null || DatabaseData.Columns.Count == 0)
            return;

        // Let the cell's LostFocus / the grid's edit-commit run first.
        await Task.Delay(50);

        SaveDatabaseRows(includeCurrentRow: true);

        var source = DatabaseData;

        // Drop empty rows added earlier and never filled in.
        foreach (var blank in source.Rows.Cast<DataRow>()
                     .Where(r => r.RowState == DataRowState.Added && !RowHasData(r)).ToList())
        {
            _checkedDatabaseRows.Remove(blank);
            source.Rows.Remove(blank);
        }

        // The grid shows rows in the view's sort order (usually the key column). A new
        // row has no key yet, and an empty value sorts FIRST -- that's why new rows used
        // to appear at the top (Excel, SQL Server, ...). So the current order is made the
        // table's own row order and the sort is dropped: the new row goes at the bottom,
        // where the user clicked "Add row", and stays there while it's typed in.
        // ImportRow keeps each row's saved/edited state, so nothing is re-saved.
        var ordered = source.Clone();
        var orderedView = new DataView(source) { Sort = source.DefaultView.Sort };
        var remap = new Dictionary<DataRow, DataRow>();
        foreach (DataRowView rowView in orderedView)
        {
            ordered.ImportRow(rowView.Row);
            remap[rowView.Row] = ordered.Rows[ordered.Rows.Count - 1];
        }

        var newRow = ordered.NewRow();
        var keyName = DatabaseTables.LastOrDefault()?.PrimaryKey;
        foreach (DataColumn column in ordered.Columns)
        {
            var isNumeric = column.DataType == typeof(int) || column.DataType == typeof(long) ||
                            column.DataType == typeof(short) || column.DataType == typeof(double) ||
                            column.DataType == typeof(decimal) || column.DataType == typeof(float);

            // Internal Database key: a temporary number (the grid shows "New"; the
            // database gives the real one when the row is saved).
            // Other sources: a numeric key column is filled with the next number as a
            // suggestion (can be overwritten), so the new person gets a unique key for
            // print history instead of an empty one.
            var suggestNext = isNumeric &&
                              (column.AutoIncrement ||
                               (!IsDatabaseEditable && !string.IsNullOrEmpty(keyName) &&
                                string.Equals(column.ColumnName, keyName, StringComparison.OrdinalIgnoreCase)));
            if (suggestNext)
            {
                var max = ordered.Rows.Cast<DataRow>()
                    .Where(r => r.RowState != DataRowState.Deleted && r[column] != DBNull.Value)
                    .Select(r => { try { return Convert.ToDecimal(r[column]); } catch { return 0m; } })
                    .DefaultIfEmpty(0m)
                    .Max();
                column.ReadOnly = false;
                newRow[column] = Convert.ChangeType(Math.Floor(max) + 1, column.DataType);
                continue;
            }
            if (column.AutoIncrement) continue;
            newRow[column] = DBNull.Value;
        }
        ordered.Rows.Add(newRow);

        // A new row can't match a search, so show everything again.
        if (!string.IsNullOrEmpty(DatabaseSearchText)) DatabaseSearchText = string.Empty;

        SelectedDatabaseRow = null;

        // Ticks follow their rows into the reordered table.
        var ticked = _checkedDatabaseRows.ToList();
        _checkedDatabaseRows.Clear();
        foreach (var row in ticked)
            if (remap.TryGetValue(row, out var moved)) _checkedDatabaseRows.Add(moved);

        ordered.DefaultView.Sort = string.Empty;
        ordered.DefaultView.RowFilter = string.Empty;
        DatabaseData = ordered;
        OnPropertyChanged(nameof(DatabaseData));
        OnPropertyChanged(nameof(DatabaseDataView));

        SelectedDatabaseRow = ordered.DefaultView
            .Cast<DataRowView>()
            .FirstOrDefault(v => v.Row == newRow);

        RefreshDatabaseSaveState();
        NotifyDocumentChanged();
    }
}
