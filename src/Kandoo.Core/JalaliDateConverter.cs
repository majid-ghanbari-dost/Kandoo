using System.Globalization;

namespace Kandoo.Core;

public readonly record struct JalaliDate(int Year, int Month, int Day);

/// <summary>Shared PersianCalendar conversion using the repository's Asia/Tehran convention.</summary>
public static class JalaliDateConverter
{
    private static readonly TimeZoneInfo TehranZone = ResolveTehranZone();

    public static DateTimeOffset ToTehran(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, TehranZone);

    public static JalaliDate FromTehran(DateTimeOffset value) =>
        FromLocalDateTime(ToTehran(value).DateTime);

    public static JalaliDate FromLocalDateTime(DateTime value)
    {
        var calendar = new PersianCalendar();
        return new JalaliDate(calendar.GetYear(value), calendar.GetMonth(value), calendar.GetDayOfMonth(value));
    }

    public static int DaysInMonth(int year, int month) =>
        new PersianCalendar().GetDaysInMonth(year, month);

    public static string Format(DateTimeOffset value)
    {
        var date = FromTehran(value);
        return FormattableString.Invariant($"{date.Year:0000}/{date.Month:00}/{date.Day:00}");
    }

    private static TimeZoneInfo ResolveTehranZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
        }
    }
}
