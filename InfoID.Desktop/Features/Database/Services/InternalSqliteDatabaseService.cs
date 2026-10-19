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
        _databasePath = GetDatabasePath(databaseFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
    }

    /// <summary>Where the Internal Database lives (%LocalAppData%\InfoID\Data\InfoID.db).
    /// Also used by Backup &amp; Restore, which includes this file.</summary>
    public static string GetDatabasePath(string databaseFileName = "InfoID.db") =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "InfoID",
            "Data",
            databaseFileName);

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

        AuditData("TableCreated", $"Internal Database table '{definition.TableName}' created",
            new
            {
                Table = definition.TableName,
                Columns = definition.Columns.Select(c => $"{c.Name} ({c.DataType}{(c.IsPrimaryKey ? ", key" : "")})").ToList(),
            });
    }

    /// <summary>Audit trail (FR-REP-4 "data changes") for the Internal Database only --
    /// not the sample database, which is demo data.</summary>
    private void AuditData(string action, string description, object after)
    {
        if (!IsMainInternalDatabase) return;
        InfoID.Desktop.Core.Services.Audit.Record(InfoID.Domain.Common.Interfaces.AuditModules.Data, action, description,
            entityName: "InternalDatabase", after: after);
    }

    private bool IsMainInternalDatabase =>
        string.Equals(Path.GetFullPath(_databasePath), Path.GetFullPath(GetDatabasePath()), StringComparison.OrdinalIgnoreCase);

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

        // Rows straight from the table are saved rows: only rows added or edited in the
        // grid afterwards show up as Added / Modified (that's what gets written back).
        table.AcceptChanges();

        return table;
    }

    public void AddMissingColumns(DatabaseTableDefinition definition)
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        var added = new List<string>();
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
            added.Add($"{column.Name} ({column.DataType})");
        }

        if (added.Count > 0)
            AuditData("ColumnsAdded", $"{added.Count} column(s) added to Internal Database table '{definition.TableName}'",
                new { Table = definition.TableName, Columns = added });
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

    /// <summary>Inserts a grid row and returns the key SQLite gave it (the table's
    /// INTEGER PRIMARY KEY / rowid). Auto-increment key columns are always left to
    /// SQLite, so a temporary number shown in the grid can never collide with a row
    /// someone else added meanwhile.</summary>
    public long InsertRow(
    string tableName,
    DataRow row)
    {
        using var connection =
            new SqliteConnection(ConnectionString);

        connection.Open();

        var columns = new List<string>();
        var parameters = new List<string>();
        var filled = new List<string>();

        using var command =
            connection.CreateCommand();

        int parameterIndex = 0;

        foreach (DataColumn column in row.Table.Columns)
        {
            if (column.AutoIncrement)
                continue;

            var value = row[column];

            columns.Add(Quote(column.ColumnName));

            var parameterName = $"$p{parameterIndex++}";
            parameters.Add(parameterName);

            command.Parameters.AddWithValue(parameterName, ToDbValue(value));

            if (value != DBNull.Value && !(value is string text && string.IsNullOrWhiteSpace(text)))
                filled.Add(column.ColumnName);
        }

        if (filled.Count == 0)
            throw new InvalidOperationException(
                "No values were available to insert.");

        command.CommandText =
            $"INSERT INTO {Quote(tableName)} " +
            $"({string.Join(", ", columns)}) " +
            $"VALUES ({string.Join(", ", parameters)});";

        command.ExecuteNonQuery();

        long newId;
        using (var idCommand = connection.CreateCommand())
        {
            idCommand.CommandText = "SELECT last_insert_rowid();";
            newId = Convert.ToInt64(idCommand.ExecuteScalar());
        }

        // Which fields were filled in -- never the values (cardholder data stays in
        // the data tables only).
        AuditData("RowAdded", $"Row {newId} added to Internal Database table '{tableName}'",
            new { Table = tableName, RowId = newId, Fields = filled });

        return newId;
    }

    /// <summary>The table's single-column primary key, or null when it has none (or a
    /// key made of several columns) -- then existing rows can't be updated or deleted
    /// one by one.</summary>
    public string? GetPrimaryKeyColumn(string tableName)
    {
        var keys = GetColumnDefinitions(tableName).Where(c => c.IsPrimaryKey).ToList();
        if (keys.Count == 1) return keys[0].Name;

        // PRAGMA table_info reports pk = 1, 2, ... for composite keys; GetColumnDefinitions
        // only flags pk == 1, so double-check there's no second key column.
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info($table) WHERE pk > 0;";
        command.Parameters.AddWithValue("$table", tableName);
        var count = Convert.ToInt32(command.ExecuteScalar());
        return count == 1 ? keys.FirstOrDefault()?.Name : null;
    }

    /// <summary>Writes the changed fields of an existing grid row back to the table,
    /// matched on its primary key as it was loaded. Returns false when no row has that
    /// key any more (deleted elsewhere). Throws when the table has no primary key.</summary>
    public bool UpdateRow(string tableName, DataRow row)
    {
        var keyColumnName = GetPrimaryKeyColumn(tableName)
            ?? throw new InvalidOperationException(
                $"Table '{tableName}' has no primary key, so changes to saved rows can't be written back.");

        if (!row.Table.Columns.Contains(keyColumnName))
            throw new InvalidOperationException($"The key column '{keyColumnName}' isn't in the grid.");

        var keyColumn = row.Table.Columns[keyColumnName]!;
        var hasOriginal = row.HasVersion(DataRowVersion.Original);
        var keyValue = hasOriginal ? row[keyColumn, DataRowVersion.Original] : row[keyColumn];
        if (keyValue == DBNull.Value)
            throw new InvalidOperationException("This row has no key value, so it can't be updated.");

        // Changed fields. A typed (non auto-increment) key may be changed too; the
        // row is still found by the key it had when it was loaded.
        var changed = new List<DataColumn>();
        foreach (DataColumn column in row.Table.Columns)
        {
            if (column.AutoIncrement) continue;
            if (ReferenceEquals(column, keyColumn) && !hasOriginal) continue;
            if (!hasOriginal || !Equals(row[column, DataRowVersion.Original], row[column]))
                changed.Add(column);
        }

        if (changed.Count == 0) return true;

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();

        var assignments = new List<string>();
        for (var i = 0; i < changed.Count; i++)
        {
            assignments.Add($"{Quote(changed[i].ColumnName)} = $p{i}");
            command.Parameters.AddWithValue($"$p{i}", ToDbValue(row[changed[i]]));
        }
        command.Parameters.AddWithValue("$key", ToDbValue(keyValue));
        command.CommandText =
            $"UPDATE {Quote(tableName)} SET {string.Join(", ", assignments)} WHERE {Quote(keyColumnName)} = $key;";

        var affected = command.ExecuteNonQuery();
        if (affected == 0) return false;

        AuditData("RowUpdated", $"Row {keyValue} changed in Internal Database table '{tableName}'",
            new { Table = tableName, RowId = Convert.ToString(keyValue, System.Globalization.CultureInfo.InvariantCulture), Fields = changed.Select(c => c.ColumnName).ToList() });
        return true;
    }

    /// <summary>Deletes rows by primary key in one transaction (all or nothing).
    /// Returns how many rows were deleted.</summary>
    public int DeleteRows(string tableName, IReadOnlyList<object> keys)
    {
        if (keys.Count == 0) return 0;

        var keyColumnName = GetPrimaryKeyColumn(tableName)
            ?? throw new InvalidOperationException(
                $"Table '{tableName}' has no primary key, so its rows can't be deleted one by one.");

        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var deleted = 0;
        foreach (var key in keys)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {Quote(tableName)} WHERE {Quote(keyColumnName)} = $key;";
            command.Parameters.AddWithValue("$key", ToDbValue(key));
            deleted += command.ExecuteNonQuery();
        }

        transaction.Commit();

        var ids = keys.Select(k => Convert.ToString(k, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        AuditData("RowsDeleted", $"{deleted} row(s) deleted from Internal Database table '{tableName}'",
            new { Table = tableName, Count = deleted, RowIds = ids.Take(100).ToList() });

        return deleted;
    }

    private static string Quote(string name) => "[" + name.Replace("]", "]]") + "]";

    /// <summary>Grid values to what SQLite stores: booleans as 0/1 (the INTEGER the
    /// table was created with), dates as ISO text, empty text as NULL.</summary>
    private static object ToDbValue(object? value) => value switch
    {
        null => DBNull.Value,
        DBNull => DBNull.Value,
        bool b => b ? 1 : 0,
        DateTime d => d.TimeOfDay == TimeSpan.Zero
            ? d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
            : d.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
        string text when string.IsNullOrWhiteSpace(text) => DBNull.Value,
        _ => value,
    };
}
