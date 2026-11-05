using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Text.Json.Serialization;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

public sealed partial class DateTimeElement : DesignerElement
{
    public override ElementType ElementType => ElementType.DateTime;

    /// <summary>Format suggestions for the editable format boxes (custom formats still allowed).</summary>
    public static string[] DateFormatPresets { get; } =
    {
        "dd-MM-yyyy", "dd/MM/yyyy", "dd.MM.yyyy", "dd MMM yyyy", "dd MMMM yyyy",
        "MM/dd/yyyy", "yyyy-MM-dd", "MMM yyyy", "MM/yy", "dddd, dd MMMM yyyy"
    };

    public static string[] TimeFormatPresets { get; } =
    {
        "HH:mm", "HH:mm:ss", "hh:mm tt", "hh:mm:ss tt"
    };

    [ObservableProperty] private DateTimeDisplayFormat _displayFormat = DateTimeDisplayFormat.DateTime;
    [ObservableProperty] private string _dateFormat = "dd-MM-yyyy";
    [ObservableProperty] private string _timeFormat = "HH:mm:ss";
    [ObservableProperty] private string _fontFamily = "Segoe UI";
    [ObservableProperty] private double _fontSize = 24;
    [ObservableProperty] private bool _bold;
    [ObservableProperty] private bool _italic;
    [ObservableProperty] private bool _underline;
    [ObservableProperty] private string _colorHex = "#000000";
    [ObservableProperty] private TextAlignmentX _horizontalAlignment = TextAlignmentX.Left;
    [ObservableProperty] private TextAlignmentY _verticalAlignment = TextAlignmentY.Top;

    /// <summary>The stored date. Only used when <see cref="Source"/> is Fixed.</summary>
    [ObservableProperty] private DateTime _value = DateTime.Now;

    /// <summary>PrintTime = the moment the card is printed (issue date); Fixed = <see cref="Value"/>.
    /// Defaults to PrintTime, so designs saved before this property existed print the real date.</summary>
    [ObservableProperty] private DateTimeSource _source = DateTimeSource.PrintTime;

    /// <summary>Added to the base date, e.g. OffsetYears = 1 for an expiry / "valid until" date.</summary>
    [ObservableProperty] private int _offsetYears;
    [ObservableProperty] private int _offsetMonths;
    [ObservableProperty] private int _offsetDays;

    /// <summary>Text around the date, e.g. "Valid until: ". Shown only when there is a date.</summary>
    [ObservableProperty] private string _prefix = string.Empty;
    [ObservableProperty] private string _suffix = string.Empty;

    public bool ShowDateFormat => DisplayFormat is DateTimeDisplayFormat.Date or DateTimeDisplayFormat.DateTime;
    public bool ShowTimeFormat => DisplayFormat is DateTimeDisplayFormat.Time or DateTimeDisplayFormat.DateTime;

    // ---- Panel helpers (not saved) ------------------------------------------------

    [JsonIgnore] public bool IsFixed => Source == DateTimeSource.Fixed;

    /// <summary>Date part of <see cref="Value"/> for the CalendarDatePicker (keeps the time).</summary>
    [JsonIgnore]
    public DateTime? FixedDate
    {
        get => Value.Date;
        set { if (value is { } d) Value = d.Date + Value.TimeOfDay; }
    }

    /// <summary>Time part of <see cref="Value"/> for the TimePicker (keeps the date).</summary>
    [JsonIgnore]
    public TimeSpan? FixedTime
    {
        get => Value.TimeOfDay;
        set { if (value is { } t) Value = Value.Date + t; }
    }

    /// <summary>What the card will show if printed now -- shown live in the panel.</summary>
    [JsonIgnore] public string PreviewText => FormatValue(DateTime.Now);

    // ---- Formatting (single source of truth for canvas, thumbnails, preview, print) ----

    /// <summary>The base date (print time or fixed) with the offset applied.</summary>
    public DateTime ResolveValue(DateTime now)
    {
        var baseValue = Source == DateTimeSource.Fixed ? Value : now;
        try
        {
            return baseValue.AddYears(OffsetYears).AddMonths(OffsetMonths).AddDays(OffsetDays);
        }
        catch (ArgumentOutOfRangeException)
        {
            return baseValue; // offset pushed past DateTime range -- ignore it rather than crash
        }
    }

    /// <summary>The final text: prefix + formatted date/time + suffix.</summary>
    public string FormatValue(DateTime now)
    {
        var value = ResolveValue(now);
        string text;
        try
        {
            text = DisplayFormat switch
            {
                DateTimeDisplayFormat.DateTime => $"{value.ToString(DateFormat)} {value.ToString(TimeFormat)}",
                DateTimeDisplayFormat.Date => value.ToString(DateFormat),
                DateTimeDisplayFormat.Time => value.ToString(TimeFormat),
                _ => string.Empty,
            };
        }
        catch (FormatException)
        {
            text = value.ToString("g"); // invalid custom format -- fall back, never crash
        }

        if (string.IsNullOrEmpty(text)) return text;
        return (Prefix ?? string.Empty) + text + (Suffix ?? string.Empty);
    }

    // ---- Change notifications for the computed properties ----------------------------

    private void RaisePreview() => OnPropertyChanged(nameof(PreviewText));

    partial void OnDisplayFormatChanged(DateTimeDisplayFormat value)
    {
        OnPropertyChanged(nameof(ShowDateFormat));
        OnPropertyChanged(nameof(ShowTimeFormat));
        RaisePreview();
    }

    partial void OnSourceChanged(DateTimeSource value)
    {
        OnPropertyChanged(nameof(IsFixed));
        RaisePreview();
    }

    partial void OnValueChanged(DateTime value)
    {
        OnPropertyChanged(nameof(FixedDate));
        OnPropertyChanged(nameof(FixedTime));
        RaisePreview();
    }

    partial void OnDateFormatChanged(string value) => RaisePreview();
    partial void OnTimeFormatChanged(string value) => RaisePreview();
    partial void OnOffsetYearsChanged(int value) => RaisePreview();
    partial void OnOffsetMonthsChanged(int value) => RaisePreview();
    partial void OnOffsetDaysChanged(int value) => RaisePreview();
    partial void OnPrefixChanged(string value) => RaisePreview();
    partial void OnSuffixChanged(string value) => RaisePreview();
}

public enum DateTimeDisplayFormat
{
    DateTime,
    Date,
    Time
}

/// <summary>Where a Date/Time element's date comes from. Append new values at the end
/// (designs store the enum value).</summary>
public enum DateTimeSource
{
    PrintTime,
    Fixed
}
