using RDCore.SDK.Runtime;
using System.Globalization;
using System.Text;

namespace RDCore.Runtime.Semantics.Conversion;

/// <summary>
/// How a <c>String</c> reads as a number when it is Let-coerced to one (<strong>MS-VBAL §5.5.1.2.4</strong>): a <c>numeric-coercion-string</c>, written by
/// the regional settings (<see cref="RegionalSettings"/>), or a hexadecimal or octal literal.
/// </summary>
/// <remarks>
/// What MS-VBA reads, recorded in Excel - which is more than the grammar of §5.5.1.2.4 says:
/// <list type="bullet">
/// <item><c>&amp;H</c> or <c>&amp;O</c> and its digits, in either case, with no sign and no type character; what it is depends on the type it is
/// converted to (<see cref="Reading.Radix"/>).</item>
/// <item>A sign before the number or after it (<c>1-</c>), or parentheses around it (<c>(1)</c>), which make it negative - never two of them.</item>
/// <item>The currency symbol before the number or after it, the sign on either side of it (<c>-$1</c>, <c>$-1</c>).</item>
/// <item>The digit grouping symbol anywhere after the first digit, even after the decimal symbol (<c>1,000.5,0</c> is 1000.5), and ignored.</item>
/// <item>An exponent, <c>e</c> or <c>d</c>, after all of it (<c>$1e3</c>), and white space around the parts.</item>
/// </list>
/// </remarks>
internal static class NumberGrammar
{
    /// <summary>
    /// What a string reads as: the bits of a hexadecimal or octal literal, or a decimal number.
    /// </summary>
    /// <param name="Radix">The value of the digits of a <c>&amp;H</c> or <c>&amp;O</c> literal, unsigned: up to 32 bits is the <c>Long</c> they are (and up to
    /// 16 an <c>Integer</c>), a negative one when the high bit is set; more than that is the number they are.</param>
    /// <param name="Digits">The digits of a decimal number, with <c>.</c> before its fraction, its sign in front: what <see cref="decimal"/> and
    /// <see cref="double"/> read invariantly.</param>
    /// <param name="Exponent">The power of ten the number is multiplied by, when it is written with one.</param>
    public readonly record struct Reading(ulong? Radix, string Digits, int? Exponent);

    /// <summary>
    /// Reads <paramref name="text"/> as a number.
    /// </summary>
    /// <returns><see langword="false"/> when it is not one; <see langword="true"/> with a <paramref name="reading"/> whose radix literal has too many
    /// digits for 64 bits is never returned: such a string is not a number either.</returns>
    public static bool TryRead(string text, RegionalSettings regional, out Reading reading)
    {
        reading = default;
        var trimmed = text.Trim();
        return trimmed.Length > 0 && (TryReadRadix(trimmed, out reading) || TryReadDecimal(trimmed, regional, out reading));
    }

    private static bool TryReadRadix(string text, out Reading reading)
    {
        reading = default;
        if (text.Length < 3 || text[0] != '&' || char.ToUpperInvariant(text[1]) is not ('H' or 'O') and var marker)
        {
            return false;
        }

        var isHex = char.ToUpperInvariant(text[1]) == 'H';
        ulong value = 0;
        foreach (var c in text.AsSpan(2))
        {
            var digit = isHex ? HexDigit(c) : c is >= '0' and <= '7' ? c - '0' : -1;
            var radix = isHex ? 16UL : 8UL;
            if (digit < 0 || value > (ulong.MaxValue - (ulong)digit) / radix)
            {
                return false;
            }

            value = value * radix + (ulong)digit;
        }

        reading = new Reading(value, string.Empty, null);
        return true;
    }

    private static int HexDigit(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static bool TryReadDecimal(string text, RegionalSettings regional, out Reading reading)
    {
        reading = default;
        var at = 0;
        var negative = false;
        var signs = 0;
        var currency = false;

        Skip(text, ref at);
        var parenthesized = At(text, at, "(");
        if (parenthesized)
        {
            at++;
            Skip(text, ref at);
        }

        // what may come before the number: a sign and a currency symbol, in either order.
        for (var round = 0; round < 2; round++)
        {
            if (TrySign(text, ref at, ref negative))
            {
                signs++;
                Skip(text, ref at);
            }
            else if (!currency && TryTake(text, ref at, regional.CurrencySymbol))
            {
                currency = true;
                Skip(text, ref at);
            }
        }

        if (signs > 1 || !TryReadMantissa(text, ref at, regional, out var digits))
        {
            return false;
        }

        if (!TryReadExponent(text, ref at, out var exponent))
        {
            return false;
        }

        // what may come after it: a currency symbol and a sign, in either order.
        for (var round = 0; round < 2; round++)
        {
            Skip(text, ref at);
            if (signs == 0 && TrySign(text, ref at, ref negative))
            {
                signs++;
            }
            else if (!currency && TryTake(text, ref at, regional.CurrencySymbol))
            {
                currency = true;
            }
        }

        Skip(text, ref at);
        if (parenthesized)
        {
            // parentheses are the sign of the number: a number in them has no other.
            if (signs > 0 || !At(text, at, ")"))
            {
                return false;
            }

            negative = true;
            at++;
            Skip(text, ref at);
        }

        if (at != text.Length)
        {
            return false;
        }

        reading = new Reading(null, (negative ? "-" : string.Empty) + digits, exponent);
        return true;
    }

    // digits, the grouping symbol after the first of them, and the decimal symbol once: the digits, with '.' for the decimal symbol.
    private static bool TryReadMantissa(string text, ref int at, RegionalSettings regional, out string digits)
    {
        var written = new StringBuilder();
        var seenDigit = false;
        var seenDecimal = false;
        while (at < text.Length)
        {
            if (char.IsAsciiDigit(text[at]))
            {
                written.Append(text[at++]);
                seenDigit = true;
            }
            else if (seenDigit && TryTake(text, ref at, regional.DigitGroupingSymbol))
            {
                // ignored: grouping is for the reader.
            }
            else if (!seenDecimal && TryTake(text, ref at, regional.DecimalSymbol))
            {
                written.Append('.');
                seenDecimal = true;
            }
            else
            {
                break;
            }
        }

        digits = written.ToString();
        return seenDigit;
    }

    // ("e" / "d") [sign] digits, with white space around its parts.
    private static bool TryReadExponent(string text, ref int at, out int? exponent)
    {
        exponent = null;
        var start = at;
        Skip(text, ref at);
        if (at >= text.Length || char.ToLowerInvariant(text[at]) is not ('e' or 'd'))
        {
            at = start;
            return true;
        }

        at++;
        Skip(text, ref at);
        var negative = false;
        TrySign(text, ref at, ref negative);
        Skip(text, ref at);
        var digitsStart = at;
        while (at < text.Length && char.IsAsciiDigit(text[at]))
        {
            at++;
        }

        if (at == digitsStart || !int.TryParse(text.AsSpan(digitsStart, at - digitsStart), NumberStyles.None, CultureInfo.InvariantCulture, out var power))
        {
            return false;
        }

        exponent = negative ? -power : power;
        return true;
    }

    private static bool TrySign(string text, ref int at, ref bool negative)
    {
        if (at < text.Length && text[at] is '+' or '-')
        {
            negative = text[at] == '-';
            at++;
            return true;
        }

        return false;
    }

    private static bool TryTake(string text, ref int at, string symbol)
    {
        if (symbol.Length > 0 && At(text, at, symbol))
        {
            at += symbol.Length;
            return true;
        }

        return false;
    }

    private static bool At(string text, int at, string symbol) => string.CompareOrdinal(text, at, symbol, 0, symbol.Length) == 0;

    private static void Skip(string text, ref int at)
    {
        while (at < text.Length && char.IsWhiteSpace(text[at]))
        {
            at++;
        }
    }
}
