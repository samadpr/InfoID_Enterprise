using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.IO;
namespace InfoID.Desktop.Features.Database.Services;

public sealed class AccessDatabaseService
{
    public string BuildConnectionString(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException(
                "Access database file path is required.");

        if (!File.Exists(filePath))
            throw new FileNotFoundException(
                "The selected Access database could not be found.",
                filePath);

        var driver = FindAccessDriver();

        if (string.IsNullOrWhiteSpace(driver))
        {
            throw new InvalidOperationException(
                "Microsoft Access ODBC Driver is not installed.");
        }

        return
            $"Driver={{{driver}}};" +
            $"DBQ={filePath};";
    }

    public void TestConnection(string connectionString)
    {
        using var connection =
            new OdbcConnection(connectionString);

        connection.Open();

        if (connection.State !=
            System.Data.ConnectionState.Open)
        {
            throw new InvalidOperationException(
                "Access database connection could not be opened.");
        }
    }

    public List<string> GetTables(string connectionString)
    {
        var tables = new List<string>();

        using var connection =
            new OdbcConnection(connectionString);

        connection.Open();

        var schema =
            connection.GetSchema("Tables");

        foreach (DataRow row in schema.Rows)
        {
            var tableType =
                row["TABLE_TYPE"]?.ToString();

            var tableName =
                row["TABLE_NAME"]?.ToString();

            if (tableType != "TABLE")
                continue;

            if (string.IsNullOrWhiteSpace(tableName))
                continue;

            // Ignore Microsoft Access system/internal tables
            if (tableName.StartsWith("MSys",
                StringComparison.OrdinalIgnoreCase))
                continue;

            if (tableName.StartsWith("USys",
                StringComparison.OrdinalIgnoreCase))
                continue;

            if (tableName.StartsWith("~",
                StringComparison.OrdinalIgnoreCase))
                continue;

            tables.Add(tableName);
        }

        return tables;
    }
    private static string? FindAccessDriver()
    {
        var registryPaths = new[]
        {
        @"SOFTWARE\ODBC\ODBCINST.INI\ODBC Drivers",
        @"SOFTWARE\WOW6432Node\ODBC\ODBCINST.INI\ODBC Drivers"
    };

        foreach (var registryPath in registryPaths)
        {
            using var key = Registry.LocalMachine.OpenSubKey(registryPath);

            if (key is null)
                continue;

            foreach (var valueName in key.GetValueNames())
            {
                if (valueName.Contains(
                        "Microsoft Access Driver",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return valueName;
                }
            }
        }

        return null;
    }
}
