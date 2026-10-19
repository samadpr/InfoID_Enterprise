using System;
using System.Data;
using System.Globalization;

namespace InfoID.Desktop.Features.Database.Services;

/// <summary>
/// Turns what someone typed into a data-grid cell into the column's real type (number,
/// date, yes/no, text), with a plain-language message when it doesn't fit. Used by the
/// designer's data grid before a value reaches the row, so a typo in a number field is
/// shown on the cell instead of failing later when the row is saved.
/// </summary>
public static class DatabaseCellValue
{
    public static bool TryParse(DataColumn column, string? text, out object value, out string? error)
    {
        error = null;
        value = DBNull.Value;

        if (string.IsNullOrWhiteSpace(text))
            return true; // empty cell = no value

        var trimmed = text.Trim();
        var type = column.DataType;

        if (type == typeof(string))
        {
            value = text;
            return true;
        }

        if (type == typeof(int) || type == typeof(long) || type == typeof(short))
        {
            if (long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.CurrentCulture, out var number) ||
                long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            {
                try
                {
                    value = Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
                    return true;
                }
                catch (OverflowException)
                {
                    error = "That number is too large for this field.";
                    return false;
                }
            }
            error = "Enter a whole number, e.g. 1024.";
            return false;
        }

        if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
        {
            if (double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out var number) ||
                double.TryParse(trimmed, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out number))
            {
                value = Convert.ChangeType(number, type, CultureInfo.InvariantCulture);
                return true;
            }
            error = "Enter a number, e.g. 12.5.";
            return false;
        }

        if (type == typeof(bool))
        {
            switch (trimmed.ToLowerInvariant())
            {
                case "1": case "true": case "yes": case "y": case "on":
                    value = true;
                    return true;
                case "0": case "false": case "no": case "n": case "off":
                    value = false;
                    return true;
            }
            error = "Enter Yes or No.";
            return false;
        }

        if (type == typeof(DateTime))
        {
            if (DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var date) ||
                DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date))
            {
                value = date;
                return true;
            }
            error = $"Enter a date, e.g. {new DateTime(2026, 12, 31).ToString("d", CultureInfo.CurrentCulture)}.";
            return false;
        }

        if (type == typeof(byte[]))
        {
            error = "Click the cell to choose an image.";
            return false;
        }

        try
        {
            value = Convert.ChangeType(trimmed, type, CultureInfo.CurrentCulture);
            return true;
        }
        catch (Exception)
        {
            error = $"This field expects a {type.Name} value.";
            return false;
        }
    }

    /// <summary>What a cell shows: dates without a time show as a date only.</summary>
    public static string Format(object? value) => value switch
    {
        null or DBNull => string.Empty,
        DateTime d when d.TimeOfDay == TimeSpan.Zero => d.ToString("d", CultureInfo.CurrentCulture),
        DateTime d => d.ToString("g", CultureInfo.CurrentCulture),
        bool b => b ? "Yes" : "No",
        _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty,
    };
}
