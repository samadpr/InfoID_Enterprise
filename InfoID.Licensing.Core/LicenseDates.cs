namespace InfoID.Licensing.Core;

/// <summary>Dates inside codes are stored as whole days since 2024-01-01 (UTC) in 16
/// bits, which covers dates up to the year 2203. Day 0 means "no date".</summary>
public static class LicenseDates
{
    public static readonly DateOnly Epoch = new(2024, 1, 1);

    public const int MaxDay = ushort.MaxValue;

    public static ushort ToDay(DateOnly date)
    {
        var days = date.DayNumber - Epoch.DayNumber;
        if (days < 1 || days > MaxDay)
            throw new ArgumentOutOfRangeException(nameof(date), date, "Date must be between 2024-01-02 and 2203.");
        return (ushort)days;
    }

    public static DateOnly? FromDay(ushort day) => day == 0 ? null : Epoch.AddDays(day);

    public static ushort Today() => ToDay(DateOnly.FromDateTime(DateTime.UtcNow));
}
