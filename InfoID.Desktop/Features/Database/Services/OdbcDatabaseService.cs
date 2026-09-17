using InfoID.Desktop.Features.Database.Models;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Odbc;
namespace InfoID.Desktop.Features.Database.Services;

public sealed class OdbcDatabaseService
{
    public ObservableCollection<string> GetTables(
    string connectionString)
    {
        var tables = new ObservableCollection<string>();

        using var connection =
            new OdbcConnection(connectionString);

        connection.Open();

        var schema =
            connection.GetSchema("Tables");

        foreach (DataRow row in schema.Rows)
        {
            var tableName =
                row["TABLE_NAME"]?.ToString();

            var tableType =
                row["TABLE_TYPE"]?.ToString();

            System.Diagnostics.Debug.WriteLine(
                $"ODBC TABLE: Name={tableName}, Type={tableType}");

            if (string.IsNullOrWhiteSpace(tableName))
                continue;

            if (!tables.Contains(tableName))
            {
                tables.Add(tableName);
            }
        }

        return tables;
    }

    public ObservableCollection<string> GetColumns(string connectionString, string tableName)
    {
        var columns = new ObservableCollection<string>();

        using var connection = new OdbcConnection(connectionString);

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = $"SELECT * FROM [{tableName}]";

        using var reader = command.ExecuteReader(CommandBehavior.SchemaOnly);

        var schemaTable = reader.GetSchemaTable();

        if (schemaTable is null)
            return columns;

        foreach (DataRow row in schemaTable.Rows)
        {
            var columnName = row["ColumnName"]?.ToString();

            if (string.IsNullOrWhiteSpace(columnName))
                continue;

            if (!columns.Contains(columnName))
            {
                columns.Add(columnName);
            }
        }

        return columns;
    }

    public ObservableCollection<DatabaseColumn> GetColumnDefinitions(
     string connectionString,
     string tableName)
    {
        var columns =
            new ObservableCollection<DatabaseColumn>();

        try
        {
            using var connection =
                new OdbcConnection(connectionString);

            connection.Open();

            using var command =
                connection.CreateCommand();

            command.CommandText =
                $"SELECT * FROM [{tableName}]";

            using var reader =
                command.ExecuteReader(CommandBehavior.SchemaOnly);

            for (int i = 0; i < reader.FieldCount; i++)
            {
                var columnName =
                    reader.GetName(i);

                if (string.IsNullOrWhiteSpace(columnName))
                    continue;

                var dataType = "Unknown";

                try
                {
                    dataType =
                        reader.GetDataTypeName(i);
                }
                catch
                {
                    // Some ODBC drivers may not provide the type name.
                }

                columns.Add(new DatabaseColumn
                {
                    Name = columnName,
                    DataType = dataType,
                    IsSelected = true,
                    IsPrimaryKey = false
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"ODBC COLUMN ERROR: {ex}");
        }

        return columns;
    }

    public DataTable GetData(string connectionString,string tableName)
    {
        using var connection = new OdbcConnection(connectionString);

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText =
            $"SELECT * FROM [{tableName}]";

        using var adapter = new OdbcDataAdapter(command);

        var table = new DataTable();

        adapter.Fill(table);

        return table;
    }
}


