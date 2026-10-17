using System.Globalization;

namespace RDCore.Runtime.StdLib.Formatting;

/// <summary>
/// A date written by a section of a user-defined date/time format, in the culture's names and the session's calendar.
/// </summary>
/// <remarks>
/// Every symbol is read in either case. A run of a symbol's letter longer than its longest form is that form and then the rest
/// of the run: <c>ddddddd</c> is <c>dddddd</c> then <c>d</c>, and <c>yyy</c> is <c>yy</c> then <c>y</c>.
/// </remarks>
internal static class DateFormatting
{
    private static readonly DateTime DayZero = new(1899, 12, 30);

    /// <summary>Writes <paramref name="moment"/>, a date in the range of dates and of the calendar, by a date section.</summary>
    public static void WriteSection(ref TextBuilder output, ReadOnlySpan<char> section, DateTime moment, in FormatSettings settings)
    {
        var dates = settings.Dates;
        var calendar = settings.Calendar;
        var twelveHour = HasAmPm(section);

        // "if m immediately follows h or hh, the minute rather than the month is displayed": what follows them is the next symbol,
        // whatever separators and literals are between.
        var afterHour = false;
        var index = 0;
        while (index < section.Length)
        {
            if (FormatSections.TryReadLiteral(section, ref index, out var literal))
            {
                output.Append(literal);
                continue;
            }

            var character = section[index];
            var run = FormatSections.RunOf(section, index);
            var taken = 1;
            var wasHour = false;
            switch (char.ToLowerInvariant(character))
            {
                case 'c':
                    WriteGeneral(ref output, moment, dates);
                    break;
                case 'd':
                    taken = Math.Min(run, 6);
                    switch (taken)
                    {
                        case <= 2:
                            output.AppendNumber(calendar.GetDayOfMonth(moment), taken);
                            break;
                        case 3:
                            output.Append(dates.GetAbbreviatedDayName(calendar.GetDayOfWeek(moment)));
                            break;
                        case 4:
                            output.Append(dates.GetDayName(calendar.GetDayOfWeek(moment)));
                            break;
                        case 5:
                            output.Append(moment, dates.ShortDatePattern, dates);
                            break;
                        default:
                            output.Append(moment, dates.LongDatePattern, dates);
                            break;
                    }

                    break;
                case 'w':
                    taken = Math.Min(run, 2);
                    output.AppendNumber(taken == 1
                        ? ((int)calendar.GetDayOfWeek(moment) - (int)settings.FirstDayOfWeek + 7) % 7 + 1
                        : calendar.GetWeekOfYear(moment, settings.WeekRule, settings.FirstDayOfWeek));
                    break;
                case 'm':
                    taken = Math.Min(run, 4);
                    switch (taken)
                    {
                        case <= 2 when afterHour:
                            output.AppendNumber(moment.Minute, taken);
                            break;
                        case <= 2:
                            output.AppendNumber(calendar.GetMonth(moment), taken);
                            break;
                        // "Hijri month names have no abbreviations".
                        case 3 when !settings.IsHijri:
                            output.Append(dates.GetAbbreviatedMonthName(calendar.GetMonth(moment)));
                            break;
                        default:
                            output.Append(dates.GetMonthName(calendar.GetMonth(moment)));
                            break;
                    }

                    break;
                case 'q':
                    output.AppendNumber((calendar.GetMonth(moment) - 1) / 3 + 1);
                    break;
                case 'y':
                    taken = run >= 4 ? 4 : run >= 2 ? 2 : 1;
                    output.AppendNumber(taken switch
                    {
                        1 => calendar.GetDayOfYear(moment),
                        2 => calendar.GetYear(moment) % 100,
                        _ => calendar.GetYear(moment),
                    }, taken == 2 ? 2 : 1);
                    break;
                case 'h':
                    taken = Math.Min(run, 2);
                    output.AppendNumber(twelveHour && moment.Hour % 12 == 0 ? 12 : twelveHour ? moment.Hour % 12 : moment.Hour, taken);
                    wasHour = true;
                    break;
                case 'n':
                    taken = Math.Min(run, 2);
                    output.AppendNumber(moment.Minute, taken);
                    break;
                case 's':
                    taken = Math.Min(run, 2);
                    output.AppendNumber(moment.Second, taken);
                    break;
                case 't' when run >= 5:
                    taken = 5;
                    output.Append(moment, dates.LongTimePattern, dates);
                    break;
                case 'a' when FormatSections.AmPmLength(section, index) is > 0 and var length:
                    taken = length;
                    WriteAmPm(ref output, section.Slice(index, length), moment.Hour < 12, dates);
                    wasHour = afterHour;
                    break;
                case '/':
                    output.Append(dates.DateSeparator);
                    wasHour = afterHour;
                    break;
                case ':':
                    output.Append(dates.TimeSeparator);
                    wasHour = afterHour;
                    break;
                default:
                    output.Append(character);
                    wasHour = afterHour;
                    break;
            }

            afterHour = wasHour;
            index += taken;
        }
    }

    // "c": "the date as ddddd and the time as ttttt, in that order", only the date if there is no time, and only the time on day 0
    // - which is how a Let-coercion to String writes a date.
    private static void WriteGeneral(ref TextBuilder output, DateTime moment, DateTimeFormatInfo dates)
    {
        if (moment.Date == DayZero)
        {
            output.Append(moment, dates.LongTimePattern, dates);
            return;
        }

        output.Append(moment, dates.ShortDatePattern, dates);
        if (moment.TimeOfDay != TimeSpan.Zero)
        {
            output.Append(' ');
            output.Append(moment, dates.LongTimePattern, dates);
        }
    }

    // AM/PM and A/P are written as they are written in the format, in its case, the part before the slash before noon and the part
    // after it from noon on; AMPM is the culture's own designator.
    private static void WriteAmPm(ref TextBuilder output, ReadOnlySpan<char> symbol, bool beforeNoon, DateTimeFormatInfo dates)
    {
        switch (symbol.Length)
        {
            case 5:
                output.Append(beforeNoon ? symbol[..2] : symbol[3..]);
                break;
            case 3:
                output.Append(beforeNoon ? symbol[0] : symbol[2]);
                break;
            default:
                output.Append(beforeNoon ? dates.AMDesignator : dates.PMDesignator);
                break;
        }
    }

    // any AM/PM symbol makes the hours of the section those of a 12-hour clock.
    private static bool HasAmPm(ReadOnlySpan<char> section)
    {
        var index = 0;
        while (index < section.Length)
        {
            if (FormatSections.TryReadLiteral(section, ref index, out _))
            {
                continue;
            }

            if (section[index] is 'a' or 'A' && FormatSections.AmPmLength(section, index) > 0)
            {
                return true;
            }

            index++;
        }

        return false;
    }
}
