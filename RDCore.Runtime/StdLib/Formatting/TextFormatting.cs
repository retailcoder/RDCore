using System.Globalization;

namespace RDCore.Runtime.StdLib.Formatting;

/// <summary>
/// A <c>String</c> written by a section of a user-defined string format: its characters in the placeholders, filled from right to
/// left unless the section has a <c>!</c>, in lowercase after a <c>&lt;</c> and uppercase after a <c>&gt;</c>.
/// </summary>
internal static class TextFormatting
{
    /// <summary>Writes <paramref name="text"/> by a string section.</summary>
    /// <remarks>
    /// <c>@</c> is a character or a space, <c>&amp;</c> a character or nothing. 👉 Characters the placeholders have no room for are
    /// not lost: they come before the first placeholder when the fill is from right to left, and after the last from left to right,
    /// so <c>Format("ABCDE", "@@")</c> is "ABCDE". A section with no placeholder writes its literals and then the whole
    /// <c>String</c>. The case applies to the <c>String</c>'s characters, and the last <c>&lt;</c> or <c>&gt;</c> is the one that does.
    /// </remarks>
    public static void WriteSection(ref TextBuilder output, ReadOnlySpan<char> section, ReadOnlySpan<char> text, TextInfo casing)
    {
        var placeholders = 0;
        var leftToRight = false;
        var lower = false;
        var upper = false;
        var index = 0;
        while (index < section.Length)
        {
            if (FormatSections.TryReadLiteral(section, ref index, out _))
            {
                continue;
            }

            switch (section[index++])
            {
                case '@' or '&':
                    placeholders++;
                    break;
                case '!':
                    leftToRight = true;
                    break;
                case '<':
                    (lower, upper) = (true, false);
                    break;
                case '>':
                    (lower, upper) = (false, true);
                    break;
            }
        }

        // right to left, the last character is in the last placeholder: the first placeholder takes what the text has before them.
        var overflow = Math.Max(0, text.Length - placeholders);
        var placeholder = 0;
        index = 0;
        while (index < section.Length)
        {
            if (FormatSections.TryReadLiteral(section, ref index, out var literal))
            {
                output.Append(literal);
                continue;
            }

            var character = section[index++];
            if (character is not ('@' or '&'))
            {
                if (character is not ('!' or '<' or '>'))
                {
                    output.Append(character);
                }

                continue;
            }

            if (!leftToRight && placeholder == 0)
            {
                Write(ref output, text[..overflow], casing, lower, upper);
            }

            var position = leftToRight ? placeholder : placeholder - (placeholders - text.Length);
            if (position >= 0 && position < text.Length && (leftToRight || position >= overflow))
            {
                Write(ref output, text.Slice(position, 1), casing, lower, upper);
            }
            else if (character == '@')
            {
                output.Append(' ');
            }

            if (leftToRight && ++placeholder == placeholders)
            {
                Write(ref output, text[Math.Min(placeholders, text.Length)..], casing, lower, upper);
            }
            else if (!leftToRight)
            {
                placeholder++;
            }
        }

        if (placeholders == 0)
        {
            Write(ref output, text, casing, lower, upper);
        }
    }

    private static void Write(ref TextBuilder output, ReadOnlySpan<char> text, TextInfo casing, bool lower, bool upper)
    {
        if (!lower && !upper)
        {
            output.Append(text);
            return;
        }

        foreach (var character in text)
        {
            output.Append(lower ? casing.ToLower(character) : casing.ToUpper(character));
        }
    }
}
