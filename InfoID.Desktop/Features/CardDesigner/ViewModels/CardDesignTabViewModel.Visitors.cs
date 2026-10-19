using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using InfoID.Application.Dtos.VisitorModule;
using InfoID.Application.Interfaces;
using InfoID.Desktop.Core.Dialogs;
using InfoID.Desktop.Features.CardDesigner.Models.Document;
using InfoID.Desktop.Features.Database.Models;
using InfoID.Desktop.Features.Visitors.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InfoID.Desktop.Features.CardDesigner.ViewModels;

/// <summary>
/// Visitor Management in the Card Designer: the "Visitors" data source (to design a badge
/// with the visitor fields) and attaching a given set of visitors to a design (Print badges
/// on the Visitors page). The rows behave like any read-only source: preview, record
/// navigation and bulk printing work unchanged, and nothing is written back.
/// </summary>
public sealed partial class CardDesignTabViewModel
{
    /// <summary>Data source picker → Visitors: the visitors of the last 30 days and upcoming
    /// ones (all of them when there are none), so the designer shows real sample data.</summary>
    private async Task ConnectVisitorsAsync()
    {
        try
        {
            var scopes = InfoID.Desktop.App.Services.GetRequiredService<IServiceScopeFactory>();
            using var scope = scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IVisitorService>();

            var visitors = (await service.GetVisitorsAsync(new VisitorFilter { Period = VisitorPeriod.Last30Days }))
                .Concat(await service.GetVisitorsAsync(new VisitorFilter { Period = VisitorPeriod.Upcoming }))
                .GroupBy(v => v.Id).Select(g => g.First())
                .ToList();
            if (visitors.Count == 0)
                visitors = (await service.GetVisitorsAsync(new VisitorFilter { Period = VisitorPeriod.All })).ToList();

            var table = VisitorBadgeData.BuildTable(visitors, new VisitorPhotoStore());
            LoadExternalTable(table, VisitorBadgeData.SourceName, VisitorBadgeData.VisitorId);

            if (visitors.Count == 0)
            {
                await _dialogService.ShowDialogAsync<MessageDialogViewModel, bool>(new MessageDialogViewModel(
                    "No visitors yet",
                    "The visitor fields are connected so you can design the badge. Import visitors on the Visitors page to see real data here."));
            }
        }
        catch (Exception ex)
        {
            Log.Error("Visitors data source error", ex);
            await _dialogService.ShowDialogAsync<MessageDialogViewModel, bool>(new MessageDialogViewModel(
                "Visitors couldn't be loaded", ex.GetBaseException().Message, isError: true));
        }
    }

    /// <summary>
    /// Shows rows from another InfoID module in this design's data grid (Visitors page →
    /// Print badges). Replaces whatever data was connected; rows typed into the Internal
    /// Database grid are saved first.
    /// </summary>
    public void AttachExternalData(DataTable table, DatabaseType type, string sourceName, string? keyColumn)
    {
        if (IsDatabaseEditable) SaveDatabaseRows(includeCurrentRow: true);

        _connectingDatabase = true;
        try
        {
            _activeDatabaseType = type;
            LoadExternalTable(table, sourceName, keyColumn);
        }
        finally
        {
            _connectingDatabase = false;
        }
        OnDatabaseTableReplaced();
    }

    /// <summary>Same steps as the other data source branches: the table definition (so
    /// print records are keyed by <paramref name="keyColumn"/>), the grid data, the fields
    /// list, and the first row as the current record.</summary>
    private void LoadExternalTable(DataTable table, string sourceName, string? keyColumn)
    {
        var definition = new DatabaseTableDefinition
        {
            DatabaseName = sourceName,
            TableName = table.TableName,
            PrimaryKey = keyColumn is not null && table.Columns.Contains(keyColumn) ? keyColumn : string.Empty,
            Columns = table.Columns.Cast<DataColumn>()
                .Select(c => new DatabaseColumn { Name = c.ColumnName, DataType = "TEXT", IsPrimaryKey = c.ColumnName == keyColumn })
                .ToList(),
        };

        DatabaseTables.Add(definition);
        _activeDatabaseTableName = table.TableName;
        DatabaseData = table;
        OnPropertyChanged(nameof(DatabaseData));
        OnPropertyChanged(nameof(DatabaseDataView));

        DatabaseFields.Clear();
        foreach (DataColumn column in table.Columns)
        {
            DatabaseFields.Add(new DatabaseField
            {
                DatabaseName = sourceName,
                TableName = table.TableName,
                ColumnName = column.ColumnName,
            });
        }

        SelectedDatabaseRow = table.DefaultView.Count > 0 ? table.DefaultView[0] : null;
    }
}
