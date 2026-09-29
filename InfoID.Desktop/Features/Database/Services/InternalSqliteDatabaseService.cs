using InfoID.Desktop.Features.Database.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace InfoID.Desktop.Features.Database.Services;

public sealed class InternalSqliteDatabaseService
{
    private readonly string _databasePath;

    public InternalSqliteDatabaseService(
     string databaseFileName = "InfoID.db")
    {
        var dataFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "InfoID",
            "Data");

        Directory.CreateDirectory(dataFolder);

        _databasePath =
            Path.Combine(
                dataFolder,
                databaseFileName);
    }

    public string DatabasePath => _databasePath;

    public string ConnectionString =>
        $"Data Source={_databasePath}";

    public void Initialize()
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();
    }
    public void InitializeSampleDatabase()
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var command =  connection.CreateCommand();

        command.CommandText = """

        DROP TABLE IF EXISTS Table1;
        DROP TABLE IF EXISTS Table2;
        DROP TABLE IF EXISTS Customers;
        DROP TABLE IF EXISTS Products;
        DROP TABLE IF EXISTS Employees;

        CREATE TABLE IF NOT EXISTS Employees
        (
            EmployeeID INTEGER PRIMARY KEY,
            SupervisorID INTEGER,
            LastName TEXT NOT NULL,
            FirstName TEXT NOT NULL,
            Position TEXT,
            BirthDate TEXT,
            HireDate TEXT,
            HomePhone TEXT,
            Extension TEXT,
            Photo TEXT,
            Notes TEXT,
            ReportsTo INTEGER,
            Salary REAL,
            SSN TEXT
        );

        INSERT OR IGNORE INTO Employees
        (
            EmployeeID,
            SupervisorID,
            LastName,
            FirstName,
            Position,
            BirthDate,
            HireDate,
            HomePhone,
            Extension,
            Photo,
            Notes,
            ReportsTo,
            Salary,
            SSN
        )
        VALUES
        (
            1,
            5,
            'Davolio',
            'Sonia',
            'Sales Representative',
            '1972-12-08',
            '1991-03-29',
            '(206)555-9857',
            '5467',
            '',
            'Education includes a BA in business administration.',
            2,
            40000.00,
            '913427512'
        ),
        (
            2,
            10,
            'Fuller',
            'Andrew',
            'Vice President, Sales',
            '1969-02-19',
            '1991-07-12',
            '(206)555-9482',
            '3457',
            '',
            'Experienced sales executive.',
            2,
            90000.00,
            '751512641'
        ),
        (
            3,
            5,
            'Leverling',
            'Janet',
            'Sales Representative',
            '1973-08-30',
            '1992-04-01',
            '(206)555-3412',
            '3355',
            '',
            'Experienced sales representative.',
            2,
            42000.00,
            '821456732'
        ),
        (
            4,
            5,
            'Peacock',
            'Margaret',
            'Sales Representative',
            '1957-09-19',
            '1993-05-03',
            '(206)555-8123',
            '5176',
            '',
            'Senior sales representative.',
            2,
            45000.00,
            '734219856'
        ),
        (
            5,
            2,
            'Buchanan',
            'Steven',
            'Sales Manager',
            '1955-03-04',
            '1993-10-17',
            '(206)555-4848',
            '3453',
            '',
            'Manages the sales team.',
            2,
            65000.00,
            '642198753'
        ),
        (
            6,
            2,
            'Suyama',
            'Michael',
            'Sales Representative',
            '1963-07-02',
            '1993-10-17',
            '(206)555-8721',
            '428',
            '',
            'Sales representative.',
            5,
            41000.00,
            '528641973'
        ),
        (
            7,
            2,
            'King',
            'Robert',
            'Sales Representative',
            '1960-05-29',
            '1994-01-02',
            '(206)555-7634',
            '465',
            '',
            'Sales representative.',
            5,
            41500.00,
            '618352947'
        ),
        (
            8,
            2,
            'Callahan',
            'Laura',
            'Inside Sales Coordinator',
            '1958-01-09',
            '1994-03-05',
            '(206)555-9481',
            '2344',
            '',
            'Coordinates internal sales activities.',
            5,
            43000.00,
            '782416539'
        );
        """;

        command.ExecuteNonQuery();
    }
    public List<string> GetTables()
    {
        var tables = new List<string>();

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            """
            SELECT name
            FROM sqlite_master
            WHERE type = 'table'
              AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
    public void CreateTable(DatabaseTableDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(definition.TableName))
            throw new ArgumentException("Table name is required.");

        if (definition.Columns.Count == 0)
            throw new ArgumentException("At least one column is required.");

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        var columnDefinitions = new List<string>();

        foreach (var column in definition.Columns)
        {
            var columnType = column.DataType switch
            {
                "Integer" => "INTEGER",
                "Real" => "REAL",
                "Boolean" => "INTEGER",
                "DateTime" => "TEXT",
                "Date" => "TEXT",
                "Text" => "TEXT",
                _ => "TEXT"
            };

            var definitionText =
                $"[{column.Name}] {columnType}";

            if (column.IsPrimaryKey)
            {
                definitionText += " PRIMARY KEY";

                if (columnType == "INTEGER")
                {
                    definitionText += " AUTOINCREMENT";
                }
            }

            columnDefinitions.Add(definitionText);
        }

        var sql =
            $"CREATE TABLE [{definition.TableName}] " +
            $"({string.Join(", ", columnDefinitions)});";

        using var command =
            connection.CreateCommand();

        command.CommandText = sql;

        command.ExecuteNonQuery();
    }
    public DataTable GetData(string tableName)
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            $"SELECT * FROM [{tableName}]";

        using var reader = command.ExecuteReader();

        var table = new DataTable();

        // Get the actual SQLite column definitions
        var columnDefinitions =
            GetColumnDefinitions(tableName);

        for (int i = 0; i < reader.FieldCount; i++)
        {
            var columnName = reader.GetName(i);

            var definition =
                columnDefinitions.FirstOrDefault(
                    x => x.Name.Equals(
                        columnName,
                        StringComparison.OrdinalIgnoreCase));

            var columnType = definition?.DataType
                             ?.ToUpperInvariant();

            var clrType = columnType switch
            {
                "INTEGER" => typeof(int),
                "REAL" => typeof(double),
                "BOOLEAN" => typeof(bool),
                "DATE" => typeof(DateTime),
                "DATETIME" => typeof(DateTime),
                "TEXT" => typeof(string),
                _ => typeof(string)
            };

            var dataColumn = table.Columns.Add( columnName,clrType);

            if (definition?.IsPrimaryKey == true &&
                clrType == typeof(int))
            {
                dataColumn.AutoIncrement = true;
                dataColumn.AutoIncrementSeed = 1;
                dataColumn.AutoIncrementStep = 1;
            }
        }

        while (reader.Read())
        {
            var row = table.NewRow();

            for (int i = 0; i < reader.FieldCount; i++)
            {
                if (reader.IsDBNull(i))
                {
                    row[i] = DBNull.Value;
                    continue;
                }

                var value = reader.GetValue(i);

                var targetType =
                    table.Columns[i].DataType;

                try
                {
                    if (targetType == typeof(bool))
                    {
                        row[i] =
                            Convert.ToInt32(value) != 0;
                    }
                    else if (targetType == typeof(DateTime))
                    {
                        row[i] =
                            DateTime.TryParse(
                                value.ToString(),
                                out var date)
                                ? date
                                : DBNull.Value;
                    }
                    else if (targetType == typeof(int))
                    {
                        row[i] =
                            Convert.ToInt32(value);
                    }
                    else if (targetType == typeof(double))
                    {
                        row[i] =
                            Convert.ToDouble(value);
                    }
                    else
                    {
                        row[i] =
                            value.ToString() ?? string.Empty;
                    }
                }
                catch
                {
                    row[i] = DBNull.Value;
                }
            }

            table.Rows.Add(row);
        }

        return table;
    }

    public void AddMissingColumns(DatabaseTableDefinition definition)
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        foreach (var column in definition.Columns)
        {
            using var checkCommand =
                connection.CreateCommand();

            checkCommand.CommandText =
                """
            SELECT COUNT(*)
            FROM pragma_table_info($tableName)
            WHERE name = $columnName;
            """;

            checkCommand.Parameters.AddWithValue(
                "$tableName",
                definition.TableName);

            checkCommand.Parameters.AddWithValue(
                "$columnName",
                column.Name);

            var exists =
                Convert.ToInt32(
                    checkCommand.ExecuteScalar()) > 0;

            if (exists)
                continue;

            var sqliteType = column.DataType switch
            {
                "Integer" => "INTEGER",
                "Real" => "REAL",
                "Boolean" => "INTEGER",
                "DateTime" => "TEXT",
                "Date" => "TEXT",
                "Text" => "TEXT",
                _ => "TEXT"
            };

            using var alterCommand =
                connection.CreateCommand();

            alterCommand.CommandText =
                $"ALTER TABLE [{definition.TableName}] " +
                $"ADD COLUMN [{column.Name}] {sqliteType};";

            alterCommand.ExecuteNonQuery();
        }
    }
    public List<DatabaseColumn> GetColumnDefinitions(
    string tableName)
    {
        var columns = new List<DatabaseColumn>();

        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        using var command =
            connection.CreateCommand();

        command.CommandText =
            $"PRAGMA table_info([{tableName}]);";

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            columns.Add(new DatabaseColumn
            {
                Name = reader["name"]?.ToString() ?? string.Empty,
                DataType = reader["type"]?.ToString() ?? string.Empty,
                IsPrimaryKey =
                    Convert.ToInt32(reader["pk"]) == 1,
                IsSelected = true
            });
        }

        return columns;
    }

    public void InsertRow(
    string tableName,
    DataRow row)
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        var columns = new List<string>();
        var parameters = new List<string>();

        using var command =
            connection.CreateCommand();

        int parameterIndex = 0;

        foreach (DataColumn column in row.Table.Columns)
        {
            // Let SQLite generate an INTEGER PRIMARY KEY
            // when the value is empty.
            if (row[column] == DBNull.Value &&
                column.AutoIncrement)
            {
                continue;
            }

            columns.Add($"[{column.ColumnName}]");

            var parameterName = $"$p{parameterIndex++}";
            parameters.Add(parameterName);

            command.Parameters.AddWithValue(
                parameterName,
                row[column] == DBNull.Value
                    ? DBNull.Value
                    : row[column]);

        }

        if (columns.Count == 0)
            throw new InvalidOperationException(
                "No values were available to insert.");

        command.CommandText =
            $"INSERT INTO [{tableName}] " +
            $"({string.Join(", ", columns)}) " +
            $"VALUES ({string.Join(", ", parameters)});";

        command.ExecuteNonQuery();
    }
}
