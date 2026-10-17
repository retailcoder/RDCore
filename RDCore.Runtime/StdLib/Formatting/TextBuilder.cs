using System.Buffers;

namespace RDCore.Runtime.StdLib.Formatting;

/// <summary>
/// The text a format writes, in a buffer the caller gives it (usually on the stack) until it outgrows it, and then in one rented
/// from the shared pool: so formatting allocates the <c>String</c> it returns, and nothing else.
/// </summary>
/// <remarks>
/// 👉 A builder that may have rented a buffer must be disposed, which gives the buffer back.
/// </remarks>
internal ref struct TextBuilder
{
    private char[]? _rented;
    private Span<char> _chars;
    private int _length;

    public TextBuilder(Span<char> initial)
    {
        _rented = null;
        _chars = initial;
        _length = 0;
    }

    public readonly int Length => _length;

    public readonly ReadOnlySpan<char> Written => _chars[.._length];

    public void Append(char character)
    {
        if (_length == _chars.Length)
        {
            Grow(1);
        }

        _chars[_length++] = character;
    }

    public void Append(char character, int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (_length > _chars.Length - count)
        {
            Grow(count);
        }

        _chars.Slice(_length, count).Fill(character);
        _length += count;
    }

    public void Append(scoped ReadOnlySpan<char> text)
    {
        if (_length > _chars.Length - text.Length)
        {
            Grow(text.Length);
        }

        text.CopyTo(_chars[_length..]);
        _length += text.Length;
    }

    /// <summary>Appends a value as it formats itself, with the format and the provider given.</summary>
    public void Append<TValue>(TValue value, scoped ReadOnlySpan<char> format, IFormatProvider? provider)
        where TValue : ISpanFormattable
    {
        int written;
        while (!value.TryFormat(_chars[_length..], out written, format, provider))
        {
            Grow(Math.Max(16, _chars.Length - _length + 1));
        }

        _length += written;
    }

    /// <summary>Appends a whole number of at least <paramref name="minimumDigits"/> ASCII digits, zero-padded.</summary>
    public void AppendNumber(int value, int minimumDigits = 1)
    {
        Span<char> digits = stackalloc char[11];
        value.TryFormat(digits, out var written, default, System.Globalization.CultureInfo.InvariantCulture);
        Append('0', minimumDigits - written);
        Append(digits[..written]);
    }

    public override readonly string ToString() => new(_chars[.._length]);

    public void Dispose()
    {
        if (_rented is { } rented)
        {
            _rented = null;
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    private void Grow(int needed)
    {
        var size = (int)Math.Clamp(Math.Max((long)_chars.Length * 2, (long)_length + needed), 256, Array.MaxLength);
        var larger = ArrayPool<char>.Shared.Rent(size);
        _chars[.._length].CopyTo(larger);
        if (_rented is { } previous)
        {
            ArrayPool<char>.Shared.Return(previous);
        }

        _rented = larger;
        _chars = larger;
    }
}
