using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace InfoID.Desktop.Features.CardDesigner.Models.Document;

public sealed partial class DateTimeElement : DesignerElement
{
    public override ElementType ElementType => ElementType.DateTime;

    [ObservableProperty]
    private DateTimeDisplayFormat _displayFormat = DateTimeDisplayFormat.DateTime;

    [ObservableProperty]
    private string _dateFormat = "dd-MM-yyyy";

    [ObservableProperty]
    private string _timeFormat = "HH:mm:ss";

    [ObservableProperty]
    private string _fontFamily = "Segoe UI";

    [ObservableProperty]
    private double _fontSize = 24;

    [ObservableProperty]
    private bool _bold;

    [ObservableProperty]
    private bool _italic;

    [ObservableProperty]
    private bool _underline;

    [ObservableProperty]
    private string _colorHex = "#000000";

    [ObservableProperty]
    private TextAlignmentX _horizontalAlignment = TextAlignmentX.Left;

    [ObservableProperty]
    private TextAlignmentY _verticalAlignment = TextAlignmentY.Top;

    [ObservableProperty]
    private DateTime _value = DateTime.Now;

    // Drive which format fields the Properties panel shows, based on the
    // selected DisplayFormat: Date-only and Time-only hide the field that
    // doesn't apply; DateTime shows both.
    public bool ShowDateFormat => DisplayFormat is DateTimeDisplayFormat.Date or DateTimeDisplayFormat.DateTime;
    public bool ShowTimeFormat => DisplayFormat is DateTimeDisplayFormat.Time or DateTimeDisplayFormat.DateTime;

    // [ObservableProperty] on DisplayFormat generates a partial
    // OnDisplayFormatChanged hook we can implement to notify the two
    // computed properties above, since they don't have their own backing
    // fields for the source generator to track automatically.
    partial void OnDisplayFormatChanged(DateTimeDisplayFormat value)
    {
        OnPropertyChanged(nameof(ShowDateFormat));
        OnPropertyChanged(nameof(ShowTimeFormat));
    }
}

public enum DateTimeDisplayFormat
{
    DateTime,
    Date,
    Time
}