using RDCore.Runtime.Semantics.Conversion;
using RDCore.SDK.Runtime;
using RDCore.SDK.Runtime.Abstract.StdLib;
using System.Collections.Concurrent;
using System.Globalization;

namespace RDCore.Runtime.StdLib.Formatting;

/// <summary>
/// What a format is written in: the regional settings of the session's environment (its culture), the calendar of the session
/// (<strong>MS-VBAL §6.1.2.11.1.8</strong>: "if the calendar is Hijri, all date format symbols [...] apply to the Hijri calendar"),
/// the regional settings a <c>String</c> is read by as a number or a date, and the week rules of the call.
/// </summary>
/// <param name="Culture">The culture numbers are written in: their separators, symbols and patterns.</param>
/// <param name="Dates">The culture's names and patterns of dates, in the session's calendar.</param>
/// <param name="Regional">The regional settings a <c>String</c> is read by as a number or a date.</param>
/// <param name="FirstDayOfWeek">The day <c>w</c> counts from, and a week of <c>ww</c> begins on.</param>
/// <param name="WeekRule">The week <c>ww</c> counts as the first of the year.</param>
internal readonly record struct FormatSettings(
    CultureInfo Culture,
    DateTimeFormatInfo Dates,
    RegionalSettings Regional,
    DayOfWeek FirstDayOfWeek,
    CalendarWeekRule WeekRule)
{
    public NumberFormatInfo Numbers => Culture.NumberFormat;

    public Calendar Calendar => Dates.Calendar;

    public bool IsHijri => Dates.Calendar is HijriCalendar;
}

/// <summary>
/// The names and patterns of dates a culture writes in a calendar, made once per culture and calendar.
/// </summary>
internal static class CalendarFormats
{
    private static readonly ConcurrentDictionary<(string Culture, VBCalendar Calendar), DateTimeFormatInfo> Made = new();

    /// <summary>
    /// The culture's date formats in <paramref name="calendar"/>: in the Gregorian calendar, the ones a Let-coercion to <c>String</c> writes
    /// with; in the Hijri calendar, the culture's own if it has one, and Arabic (Saudi Arabia)'s otherwise - "symbols that result in text
    /// display [...] display the string (English or Arabic) associated with that symbol".
    /// </summary>
    public static DateTimeFormatInfo Of(CultureInfo culture, VBCalendar calendar)
        => calendar is not VBCalendar.VBCalHijri && culture.Calendar is GregorianCalendar
            ? culture.DateTimeFormat
            : Made.GetOrAdd((culture.Name, calendar), static (key, culture) => Make(culture, key.Calendar), culture);

    private static DateTimeFormatInfo Make(CultureInfo culture, VBCalendar calendar)
    {
        var gregorian = ValueConversions.DateFormatOf(culture);
        if (calendar is not VBCalendar.VBCalHijri)
        {
            return DateTimeFormatInfo.ReadOnly(gregorian);
        }

        // borrowed names and patterns keep the culture's own separators: "/" and ":" are the environment's.
        // 👉 a platform without the data of Arabic (Saudi Arabia) has no Hijri names to write: there the Gregorian ones are written.
        return HijriOf(culture, culture) ?? HijriOf(Arabic(), culture) ?? DateTimeFormatInfo.ReadOnly(gregorian);
    }

    private static DateTimeFormatInfo? HijriOf(CultureInfo? named, CultureInfo culture)
    {
        if (named?.OptionalCalendars.OfType<HijriCalendar>().FirstOrDefault() is not { } hijri)
        {
            return null;
        }

        var format = (DateTimeFormatInfo)named.DateTimeFormat.Clone();
        format.Calendar = hijri;
        format.DateSeparator = culture.DateTimeFormat.DateSeparator;
        format.TimeSeparator = culture.DateTimeFormat.TimeSeparator;
        return DateTimeFormatInfo.ReadOnly(format);
    }

    private static CultureInfo? Arabic()
    {
        try
        {
            return CultureInfo.GetCultureInfo("ar-SA");
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
