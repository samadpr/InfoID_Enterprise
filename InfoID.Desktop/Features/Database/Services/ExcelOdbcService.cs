using System;
using System.Data;
using System.Data.Odbc;

namespace InfoID.Desktop.Features.Database.Services;

public sealed class ExcelOdbcService
{
    public bool TestConnection(
        string connectionString,
        out string errorMessage)
    {
        errorMessage = string.Empty;

        try
        {
            using var connection = new OdbcConnection(connectionString);

            connection.Open();

            return connection.State == ConnectionState.Open;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public DataTable GetSheets(string connectionString)
    {
        using var connection = new OdbcConnection(connectionString);

        connection.Open();

        return connection.GetSchema("Tables");
    }

    public DataTable ReadSheet(
        string connectionString,
        string sheetName)
    {
        using var connection = new OdbcConnection(connectionString);

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = $"SELECT * FROM [{sheetName}]";

        using var adapter = new OdbcDataAdapter(command);

        var table = new DataTable();

        adapter.Fill(table);

        return table;
    }
}
