namespace RDCore.Runtime.StdLib.Formatting;

/// <summary>
/// What a section of a user-defined format is a format of, by the characters it uses outside its literals.
/// </summary>
internal enum FormatSectionKind
{
    /// <summary>Nothing but literals.</summary>
    Literal,

    /// <summary>A number: a digit placeholder, a decimal or thousand separator, a percentage, a scientific exponent.</summary>
    Number,

    /// <summary>A date or a time: any date or time symbol.</summary>
    Date,

    /// <summary>A string: a character placeholder (<c>@</c>, <c>&amp;</c>), a case (<c>&lt;</c>, <c>&gt;</c>) or a fill order (<c>!</c>).</summary>
    Text,
}

/// <summary>A section of a user-defined format: where it is in the format, and what it formats.</summary>
internal readonly record struct FormatSection(int Start, int Length, FormatSectionKind Kind)
{
    public bool IsEmpty => Length == 0;

    public ReadOnlySpan<char> Of(ReadOnlySpan<char> format) => format.Slice(Start, Length);
}

/// <summary>
/// The sections of a user-defined format, and its literals: <c>"ABC"</c> displays what is between the quotes, and <c>\</c> the
/// character after it, whatever either would otherwise mean.
/// </summary>
internal static class FormatSections
{
    /// <summary>A format has at most four sections: positive, negative, zero and Null; a string format has two.</summary>
    public const int Most = 4;

    /// <summary>
    /// Reads a literal at <paramref name="index"/>, if one begins there, and moves past it: a quoted run (to the end of the section if
    /// its closing quote is missing), or the character after a backslash (nothing after a backslash that ends the section).
    /// </summary>
    public static bool TryReadLiteral(ReadOnlySpan<char> section, ref int index, out ReadOnlySpan<char> literal)
    {
        switch (section[index])
        {
            case '"':
                var rest = section[(index + 1)..];
                var length = rest.IndexOf('"');
                literal = length < 0 ? rest : rest[..length];
                index += length < 0 ? 1 + rest.Length : 2 + length;
                return true;
            case '\\':
                literal = index + 1 < section.Length ? section.Slice(index + 1, 1) : [];
                index += 1 + literal.Length;
                return true;
            default:
                literal = default;
                return false;
        }
    }

    /// <summary>
    /// Splits <paramref name="format"/> at each semicolon outside a literal, into at most <see cref="Most"/> sections, and gives how many
    /// there are. 👉 A semicolon after the fourth section ends it, and what follows is no section.
    /// </summary>
    public static int Split(ReadOnlySpan<char> format, Span<FormatSection> sections)
    {
        var count = 0;
        var start = 0;
        var index = 0;
        while (index < format.Length)
        {
            if (TryReadLiteral(format, ref index, out _))
            {
                continue;
            }

            if (format[index] == ';')
            {
                sections[count++] = Section(format, start, index);
                if (count == Most)
                {
                    return count;
                }

                start = index + 1;
            }

            index++;
        }

        sections[count++] = Section(format, start, Math.Min(index, format.Length));
        return count;
    }

    private static FormatSection Section(ReadOnlySpan<char> format, int start, int end)
        => new(start, end - start, KindOf(format[start..end]));

    /// <summary>
    /// What a section formats. A character placeholder, a case or a fill order makes it a string format; otherwise any date or time
    /// symbol makes it a date format, so a letter that is one is never a literal unless it is quoted or escaped; otherwise a digit
    /// placeholder, a separator, a percentage or an exponent makes it a number format.
    /// </summary>
    public static FormatSectionKind KindOf(ReadOnlySpan<char> section)
    {
        bool text = false, date = false, number = false;
        var index = 0;
        while (index < section.Length)
        {
            if (TryReadLiteral(section, ref index, out _))
            {
                continue;
            }

            switch (char.ToLowerInvariant(section[index]))
            {
                case '@' or '&' or '<' or '>' or '!':
                    text = true;
                    break;
                case '0' or '#' or '%' or '.' or ',':
                    number = true;
                    break;
                case 'e' when IsExponent(section, index):
                    number = true;
                    index++;
                    break;
                case 'c' or 'd' or 'w' or 'm' or 'q' or 'y' or 'h' or 'n' or 's':
                    date = true;
                    break;
                case 't' when RunOf(section, index) >= 5:
                    date = true;
                    break;
                case 'a' when AmPmLength(section, index) is > 0 and var length:
                    date = true;
                    index += length - 1;
                    break;
            }

            index++;
        }

        return text ? FormatSectionKind.Text
            : date ? FormatSectionKind.Date
            : number ? FormatSectionKind.Number
            : FormatSectionKind.Literal;
    }

    /// <summary>Whether an <c>E</c> or an <c>e</c> at <paramref name="index"/> is followed by the sign of an exponent.</summary>
    public static bool IsExponent(ReadOnlySpan<char> section, int index)
        => index + 1 < section.Length && section[index + 1] is '+' or '-';

    /// <summary>How many times the letter at <paramref name="index"/> is repeated from there, in either case.</summary>
    public static int RunOf(ReadOnlySpan<char> section, int index)
    {
        var letter = char.ToLowerInvariant(section[index]);
        var end = index + 1;
        while (end < section.Length && char.ToLowerInvariant(section[end]) == letter)
        {
            end++;
        }

        return end - index;
    }

    /// <summary>The length of the AM/PM symbol at <paramref name="index"/>, in any case: 5 for <c>AM/PM</c>, 4 for <c>AMPM</c>, 3 for <c>A/P</c>; 0 for none.</summary>
    public static int AmPmLength(ReadOnlySpan<char> section, int index)
    {
        var rest = section[index..];
        return rest.StartsWith("am/pm", StringComparison.OrdinalIgnoreCase) ? 5
            : rest.StartsWith("ampm", StringComparison.OrdinalIgnoreCase) ? 4
            : rest.StartsWith("a/p", StringComparison.OrdinalIgnoreCase) ? 3
            : 0;
    }
}
