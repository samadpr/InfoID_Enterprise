using InfoID.Desktop.Features.Database.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;

namespace InfoID.Desktop.Features.Database.Services;

public sealed class SqliteDatabaseService
{
    public string BuildConnectionString(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException(
                "SQLite database file path is required.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException(
                "The selected SQLite database could not be found.",
                filePath);

        return $"Data Source={filePath};";
    }

    public void TestConnection(string connectionString)
    {
        using var connection =
            new SqliteConnection(connectionString);

        connection.Open();

        if (connection.State != ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "SQLite database connection could not be opened.");
        }
    }

    public ObservableCollection<string> GetTables(
        string connectionString)
    {
        var tables =
            new ObservableCollection<string>();

        using var connection =
            new SqliteConnection(connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText = @"
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
            AND name NOT LIKE 'sqlite_%'
            ORDER BY name;";

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            var tableName =
                reader["name"]?.ToString();

            if (string.IsNullOrWhiteSpace(tableName))
                continue;

            tables.Add(tableName);
        }

        return tables;
    }

    public ObservableCollection<DatabaseColumn> GetColumnDefinitions(
        string connectionString,
        string tableName)
    {
        var columns =
            new ObservableCollection<DatabaseColumn>();

        using var connection =
            new SqliteConnection(connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            $"PRAGMA table_info([{tableName.Replace("]", "]]")}]);";

        using var reader =
            command.ExecuteReader();

        while (reader.Read())
        {
            var columnName =
                reader["name"]?.ToString();

            if (string.IsNullOrWhiteSpace(columnName))
                continue;

            var dataType =
                reader["type"]?.ToString() ?? "Unknown";

            var primaryKey =
                Convert.ToInt32(reader["pk"]) == 1;

            columns.Add(
                new DatabaseColumn
                {
                    Name = columnName,
                    DataType = dataType,
                    IsPrimaryKey = primaryKey,
                    IsSelected = true
                });
        }

        return columns;
    }

    public DataTable GetData(
        string connectionString,
        string tableName)
    {
        using var connection =
            new SqliteConnection(connectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            $"SELECT * FROM [{tableName.Replace("]", "]]")}];";

        using var reader =
            command.ExecuteReader();

        var table =
            new DataTable();

        table.Load(reader);

        return table;
    }
}
