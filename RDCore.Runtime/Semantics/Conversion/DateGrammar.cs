namespace RDCore.Runtime.Semantics.Conversion;

/// <summary>
/// The grammar of a date and a time (<strong>MS-VBAL §3.3.3</strong> <c>date-or-time</c>), read from text: a date literal's contents, and a <c>String</c>
/// Let-coerced to a <c>Date</c>.
/// </summary>
/// <remarks>
/// <code>
/// date-or-time = (date-value 1*WSC time-value) / date-value / time-value
/// date-value = left-date-value date-separator middle-date-value [date-separator right-date-value]
/// date-separator = 1*WSC / (*WSC ("/" / "-" / ",") *WSC)
/// time-value = (hour-value ampm) / (hour-value time-separator minute-value [time-separator second-value] [ampm])
/// time-separator = *WSC (":" / ".") *WSC
/// ampm = *WSC ("am" / "pm" / "a" / "p")
/// </code>
/// Each value of a date is a <c>decimal-literal</c> or an English month name or abbreviation, at most one of them a month name, and which of the numbers
/// is the month, the day and the year is decided by which can be: the rules of §3.3.3, in their order. A year written with one or two digits is in the
/// hundred years that end with the two-digit-year window's last year, a regional setting: 2049 by default, as it is on Windows today, which makes 30
/// 2030 and 50 1950 (recorded in Excel).
/// </remarks>
internal static class DateGrammar
{
    private static readonly string[] MonthNames =
        ["january", "february", "march", "april", "may", "june", "july", "august", "september", "october", "november", "december"];

    private static readonly string[] MonthAbbreviations =
        ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    /// <summary>
    /// Reads <paramref name="text"/> as a date, a time, or a date and a time.
    /// </summary>
    /// <param name="text">The text, which may be surrounded by white space.</param>
    /// <param name="currentYear">The year of a date that does not say one - "an implementation-defined default year".</param>
    /// <param name="value">The date and time it reads as: the time of a date that says none is midnight, and the date of a time that says none is
    /// 1899-12-30, the date of serial 0.</param>
    /// <param name="twoDigitYearMax">The last year a year written with one or two digits can be.</param>
    /// <returns><see langword="false"/> when the text is not a <c>date-or-time</c>.</returns>
    public static bool TryRead(string text, int currentYear, out DateTime value, int twoDigitYearMax = DefaultTwoDigitYearMax)
    {
        var years = new Years(currentYear, twoDigitYearMax);
        value = default;
        var trimmed = text.Trim(' ', '\t');
        if (trimmed.Length == 0)
        {
            return false;
        }

        // a date and a time: the date is everything before some run of white space, and the time everything after it.
        for (var split = 1; split < trimmed.Length; split++)
        {
            if (IsWhiteSpace(trimmed[split]) && !IsWhiteSpace(trimmed[split - 1])
                && TryReadDate(trimmed[..split], years, out var onDate)
                && TryReadTime(trimmed[split..].TrimStart(' ', '\t'), out var atTime))
            {
                value = onDate + atTime;
                return true;
            }
        }

        if (TryReadDate(trimmed, years, out var date))
        {
            value = date;
            return true;
        }

        if (TryReadTime(trimmed, out var time))
        {
            value = new DateTime(1899, 12, 30) + time;
            return true;
        }

        return false;
    }

    private static bool IsWhiteSpace(char c) => c is ' ' or '\t';

    // one value of a date: a number, or a month name, which is the month it names (1 to 12) and whether it is one.
    private readonly record struct Part(int Number, bool IsMonthName);

    // the year of a date that says none, and the hundred years a year written with one or two digits is in: the ones that end with TwoDigitMax.
    private readonly record struct Years(int Current, int TwoDigitMax)
    {
        public int Of(int written)
        {
            if (written >= 100)
            {
                return written;
            }

            var year = TwoDigitMax / 100 * 100 + written;
            return year > TwoDigitMax ? year - 100 : year;
        }
    }

    private static bool TryReadDate(string text, Years years, out DateTime date)
    {
        date = default;
        var parts = new List<Part>();
        var at = 0;
        while (true)
        {
            if (!TryReadPart(text, ref at, out var part))
            {
                return false;
            }

            parts.Add(part);
            if (at == text.Length)
            {
                break;
            }

            if (parts.Count == 3 || !TryReadDateSeparator(text, ref at))
            {
                return false;
            }
        }

        if (parts.Count < 2 || parts.Count(part => part.IsMonthName) > 1)
        {
            return false;
        }

        return parts.Any(part => part.IsMonthName)
            ? TryInterpretWithAMonthName(parts, years, out date)
            : TryInterpretNumbers(parts, years, out date);
    }

    private static bool TryReadPart(string text, ref int at, out Part part)
    {
        part = default;
        var start = at;
        if (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            while (at < text.Length && char.IsAsciiDigit(text[at]))
            {
                at++;
            }

            // a number too big to be any part of a date is not a valid date.
            if (!int.TryParse(text.AsSpan(start, at - start), out var number))
            {
                return false;
            }

            part = new Part(number, false);
            return true;
        }

        while (at < text.Length && char.IsAsciiLetter(text[at]))
        {
            at++;
        }

        var name = text[start..at].ToLowerInvariant();
        var month = Array.IndexOf(MonthNames, name) is var full and >= 0 ? full : Array.IndexOf(MonthAbbreviations, name);
        if (month < 0)
        {
            return false;
        }

        part = new Part(month + 1, true);
        return true;
    }

    // date-separator = 1*WSC / (*WSC ("/" / "-" / ",") *WSC)
    private static bool TryReadDateSeparator(string text, ref int at)
    {
        var start = at;
        SkipWhiteSpace(text, ref at);
        if (at < text.Length && text[at] is '/' or '-' or ',')
        {
            at++;
            SkipWhiteSpace(text, ref at);
            return true;
        }

        return at > start;
    }

    private static void SkipWhiteSpace(string text, ref int at)
    {
        while (at < text.Length && IsWhiteSpace(text[at]))
        {
            at++;
        }
    }

    private static bool LegalMonth(int month) => month is >= 1 and <= 12;

    private static bool LegalDay(int month, int day, int year)
        => LegalMonth(month) && year is >= 100 and <= 9999 && day >= 1 && day <= DateTime.DaysInMonth(year, month);

    /// <summary>
    /// The last year a year written with one or two digits can be, unless the regional settings say otherwise.
    /// </summary>
    public const int DefaultTwoDigitYearMax = 2049;

    private static bool TryMake(int year, int month, int day, out DateTime date)
    {
        date = default;
        if (!LegalDay(month, day, year))
        {
            return false;
        }

        date = new DateTime(year, month, day);
        return true;
    }

    // "If L and M are numbers and R is not present", "If L, M, and R are numbers" - in that order.
    private static bool TryInterpretNumbers(List<Part> parts, Years years, out DateTime date)
    {
        date = default;
        var (l, m) = (parts[0].Number, parts[1].Number);
        var currentYear = years.Current;
        if (parts.Count == 2)
        {
            return LegalMonth(l) && LegalDay(l, m, currentYear) ? TryMake(currentYear, l, m, out date)
                : LegalMonth(m) && LegalDay(m, l, currentYear) ? TryMake(currentYear, m, l, out date)
                : LegalMonth(l) ? TryMake(years.Of(m), l, 1, out date)
                : LegalMonth(m) && TryMake(years.Of(l), m, 1, out date);
        }

        var r = parts[2].Number;
        return LegalMonth(l) && LegalDay(l, m, years.Of(r)) ? TryMake(years.Of(r), l, m, out date)
            : LegalMonth(m) && LegalDay(m, r, years.Of(l)) ? TryMake(years.Of(l), m, r, out date)
            : LegalMonth(m) && LegalDay(m, l, years.Of(r)) && TryMake(years.Of(r), m, l, out date);
    }

    // "If either L or M is not a number and R is not present", "Otherwise, R is present and one of L, M, and R is not a number".
    private static bool TryInterpretWithAMonthName(List<Part> parts, Years years, out DateTime date)
    {
        date = default;
        var month = parts.First(part => part.IsMonthName).Number;
        var numbers = parts.Where(part => !part.IsMonthName).Select(part => part.Number).ToArray();
        if (numbers.Length == 1)
        {
            var n = numbers[0];
            return LegalDay(month, n, years.Current) ? TryMake(years.Current, month, n, out date) : TryMake(years.Of(n), month, 1, out date);
        }

        var (n1, n2) = (numbers[0], numbers[1]);
        return LegalDay(month, n1, years.Of(n2)) ? TryMake(years.Of(n2), month, n1, out date) : TryMake(years.Of(n1), month, n2, out date);
    }

    // time-value = (hour-value ampm) / (hour-value time-separator minute-value [time-separator second-value] [ampm])
    private static bool TryReadTime(string text, out TimeSpan time)
    {
        time = default;
        var at = 0;
        if (!TryReadNumber(text, ref at, out var hour))
        {
            return false;
        }

        var values = new List<int> { hour };
        while (values.Count < 3 && TryReadTimeSeparator(text, ref at))
        {
            if (!TryReadNumber(text, ref at, out var next))
            {
                return false;
            }

            values.Add(next);
        }

        var ampm = TryReadAmPm(text, ref at);
        if (at != text.Length || (values.Count == 1 && ampm is null))
        {
            return false;
        }

        var (h, m, s) = (values[0], values.Count > 1 ? values[1] : 0, values.Count > 2 ? values[2] : 0);
        if (h > 23 || m > 59 || s > 59)
        {
            return false;
        }

        // "pm" adds 12 to an hour of 0 to 11, "am" makes 12 midnight, and either means nothing after 12.
        h = ampm switch
        {
            'p' when h <= 11 => h + 12,
            'a' when h == 12 => 0,
            _ => h,
        };

        time = new TimeSpan(h, m, s);
        return true;
    }

    private static bool TryReadNumber(string text, ref int at, out int number)
    {
        number = 0;
        var start = at;
        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
        }

        return at > start && int.TryParse(text.AsSpan(start, at - start), out number);
    }

    // time-separator = *WSC (":" / ".") *WSC - put back when what follows is not one.
    private static bool TryReadTimeSeparator(string text, ref int at)
    {
        var start = at;
        SkipWhiteSpace(text, ref at);
        if (at < text.Length && text[at] is ':' or '.')
        {
            at++;
            SkipWhiteSpace(text, ref at);
            return true;
        }

        at = start;
        return false;
    }

    // ampm = *WSC ("am" / "pm" / "a" / "p"): 'a' or 'p', or null when there is none.
    private static char? TryReadAmPm(string text, ref int at)
    {
        var start = at;
        SkipWhiteSpace(text, ref at);
        var rest = text[at..].ToLowerInvariant();
        foreach (var marker in (string[])["am", "pm", "a", "p"])
        {
            if (rest == marker)
            {
                at = text.Length;
                return marker[0];
            }
        }

        at = start;
        return null;
    }
}
