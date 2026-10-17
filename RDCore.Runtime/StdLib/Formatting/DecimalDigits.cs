using RDCore.SDK.Model.Types;
using RDCore.SDK.Model.Values.Abstract;
using RDCore.SDK.Model.Values.Intrinsic;
using System.Globalization;

namespace RDCore.Runtime.StdLib.Formatting;

/// <summary>
/// A number to format, as the value it is: a whole number and a Currency or a Decimal exactly, and a floating-point number to
/// the significant digits its type writes (<strong>MS-VBAL §5.5.1.2.4</strong>: 15 for a Double, 7 for a Single).
/// </summary>
internal readonly struct NumberValue
{
    private enum Representation { Whole, Fixed, Floating }

    private readonly Representation _representation;
    private readonly long _whole;
    private readonly decimal _fixed;
    private readonly double _floating;

    private NumberValue(Representation representation, long whole, decimal @fixed, double floating, int significantDigits)
    {
        _representation = representation;
        _whole = whole;
        _fixed = @fixed;
        _floating = floating;
        SignificantDigits = significantDigits;
    }

    public static NumberValue Zero => Whole(0);

    public static NumberValue Whole(long value) => new(Representation.Whole, value, default, default, default);

    public static NumberValue Fixed(decimal value) => new(Representation.Fixed, default, value, default, default);

    public static NumberValue Floating(double value, int significantDigits) => new(Representation.Floating, default, default, value, significantDigits);

    /// <summary>How many significant digits a floating-point number is written to.</summary>
    public int SignificantDigits { get; }

    public int Sign => _representation switch
    {
        Representation.Whole => Math.Sign(_whole),
        Representation.Fixed => Math.Sign(_fixed),
        _ => double.IsNaN(_floating) ? 0 : Math.Sign(_floating),
    };

    public bool IsFinite => _representation is not Representation.Floating || double.IsFinite(_floating);

    /// <summary>The number as a value of the type that writes it the same way: a LongLong, a Decimal, a Single or a Double.</summary>
    public VBNumericTypedValue ToTypedValue() => _representation switch
    {
        Representation.Whole => new VBLongLongValue(_whole),
        Representation.Fixed => new VBDecimalValue(_fixed),
        _ when SignificantDigits == VBSingleType.SignificantIntegerDigits => new VBSingleValue((float)_floating),
        _ => new VBDoubleValue(_floating),
    };

    /// <summary>Writes the digits of the number's magnitude into <paramref name="digits"/>.</summary>
    public void WriteTo(ref DecimalDigits digits)
    {
        switch (_representation)
        {
            case Representation.Whole:
                digits.SetWhole(_whole < 0 ? (ulong)(-(_whole + 1)) + 1 : (ulong)_whole);
                break;
            case Representation.Fixed:
                Span<char> text = stackalloc char[48];
                _fixed.TryFormat(text, out var written, default, CultureInfo.InvariantCulture);
                digits.SetPlain(text[..written]);
                break;
            default:
                digits.SetFloating(Math.Abs(_floating), SignificantDigits);
                break;
        }
    }
}

/// <summary>
/// The decimal digits of a magnitude, with no leading or trailing zero, and where its decimal point falls among them: the value
/// is <c>0.d₁d₂…dₙ × 10^Point</c>, and zero has no digits. Scaling by a power of ten moves the point, and rounding drops digits,
/// so a percentage, a thousands scale and scientific notation are all exact.
/// </summary>
/// <remarks>
/// The digits live in a buffer the caller gives (on the stack): 30 digits hold a Decimal's 29 and a carry.
/// </remarks>
internal ref struct DecimalDigits
{
    private readonly Span<byte> _digits;

    public DecimalDigits(Span<byte> buffer)
    {
        _digits = buffer;
        Count = 0;
        Point = 0;
    }

    /// <summary>How many significant digits there are; 0 for zero.</summary>
    public int Count { get; private set; }

    /// <summary>How many of the digits are before the decimal point: more than <see cref="Count"/> for a number with zeros before its point, and negative for one with zeros after it.</summary>
    public int Point { get; private set; }

    public readonly bool IsZero => Count == 0;

    /// <summary>How many digits the whole part has: none for a number below 1.</summary>
    public readonly int IntegerDigits => Count == 0 ? 0 : Math.Max(Point, 0);

    /// <summary>How many digits the fraction has, up to its last significant one.</summary>
    public readonly int FractionDigits => Math.Max(Count - Point, 0);

    /// <summary>The digit <paramref name="position"/> places after the first digit of the whole part (0 is the first), a zero past the significant digits.</summary>
    public readonly int IntegerDigit(int position) => position < Count ? _digits[position] : 0;

    /// <summary>The <paramref name="position"/>-th digit after the decimal point, from 0.</summary>
    public readonly int FractionDigit(int position)
    {
        var index = Point + position;
        return index >= 0 && index < Count ? _digits[index] : 0;
    }

    /// <summary>Multiplies the magnitude by <c>10^exponent</c>.</summary>
    public void Scale(int exponent)
    {
        if (Count > 0)
        {
            Point += exponent;
        }
    }

    /// <summary>Puts the point after <paramref name="integerDigits"/> digits and gives the power of ten that takes away; 0 for zero.</summary>
    public int Normalize(int integerDigits)
    {
        if (Count == 0)
        {
            return 0;
        }

        var exponent = Point - integerDigits;
        Point = integerDigits;
        return exponent;
    }

    /// <summary>Rounds to <paramref name="fractionDigits"/> places after the point, half away from zero.</summary>
    public void Round(int fractionDigits)
    {
        var kept = Point + fractionDigits;
        if (kept >= Count)
        {
            return;
        }

        if (kept < 0)
        {
            Count = 0;
            Point = 0;
            return;
        }

        var roundsUp = _digits[kept] >= 5;
        Count = kept;
        if (roundsUp)
        {
            var index = kept - 1;
            while (index >= 0 && _digits[index] == 9)
            {
                index--;
            }

            if (index < 0)
            {
                // every digit kept was a 9, or none was kept: the carry is a 1 one place up.
                _digits[0] = 1;
                Count = 1;
                Point++;
                return;
            }

            _digits[index]++;
            Count = index + 1;
        }

        TrimTrailingZeros();
    }

    internal void SetWhole(ulong magnitude)
    {
        Count = 0;
        for (var rest = magnitude; rest > 0; rest /= 10)
        {
            _digits[Count++] = (byte)(rest % 10);
        }

        _digits[..Count].Reverse();
        Point = Count;
        TrimTrailingZeros();
    }

    // digits written plainly, a sign and a point among them: what a Decimal writes.
    internal void SetPlain(scoped ReadOnlySpan<char> text)
    {
        Count = 0;
        Point = 0;
        var beforePoint = true;
        foreach (var character in text)
        {
            if (character == '.')
            {
                beforePoint = false;
                continue;
            }

            if (!char.IsAsciiDigit(character))
            {
                continue;
            }

            if (Count == 0 && character == '0')
            {
                // a leading zero is no digit: one after the point moves the point instead.
                if (!beforePoint)
                {
                    Point--;
                }

                continue;
            }

            _digits[Count++] = (byte)(character - '0');
            if (beforePoint)
            {
                Point++;
            }
        }

        if (Count == 0)
        {
            Point = 0;
        }

        TrimTrailingZeros();
    }

    // a finite magnitude to its significant digits, rounded there as "E" rounds them: d.ddd…E±xxx.
    internal void SetFloating(double magnitude, int significantDigits)
    {
        Count = 0;
        Point = 0;
        if (magnitude == 0 || !double.IsFinite(magnitude))
        {
            return;
        }

        Span<char> format = stackalloc char[3];
        format[0] = 'E';
        (significantDigits - 1).TryFormat(format[1..], out var formatLength, default, CultureInfo.InvariantCulture);
        Span<char> text = stackalloc char[40];
        magnitude.TryFormat(text, out var written, format[..(1 + formatLength)], CultureInfo.InvariantCulture);
        var scientific = text[..written];
        var exponentAt = scientific.IndexOf('E');
        foreach (var character in scientific[..exponentAt])
        {
            if (char.IsAsciiDigit(character))
            {
                _digits[Count++] = (byte)(character - '0');
            }
        }

        Point = int.Parse(scientific[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture) + 1;
        TrimTrailingZeros();
    }

    private void TrimTrailingZeros()
    {
        while (Count > 0 && _digits[Count - 1] == 0)
        {
            Count--;
        }

        if (Count == 0)
        {
            Point = 0;
        }
    }
}
