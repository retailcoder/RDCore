using System.Collections.Immutable;
using System.Globalization;

namespace RDCore.External.Interaction;

/// <summary>
/// The keys a keystroke is pressed with.
/// </summary>
[Flags]
public enum KeyModifiers
{
    /// <summary>No modifier.</summary>
    None = 0,
    /// <summary><kbd>Shift</kbd>, written <c>+</c>.</summary>
    Shift = 1,
    /// <summary><kbd>Ctrl</kbd>, written <c>^</c>.</summary>
    Control = 2,
    /// <summary><kbd>Alt</kbd>, written <c>%</c>.</summary>
    Alt = 4,
}

/// <summary>
/// One keystroke of a <c>SendKeys</c> string: a character, or a named key such as <c>ENTER</c> or <c>F5</c>, pressed with its modifiers.
/// </summary>
/// <param name="Key">The name of the key (<see cref="SendKeysParser.KeyNames"/>), or <see langword="null"/> for a character.</param>
/// <param name="Character">The character, when <paramref name="Key"/> is <see langword="null"/>.</param>
/// <param name="Modifiers">The keys held down while it is pressed.</param>
public readonly record struct KeyStroke(string? Key, char Character, KeyModifiers Modifiers)
{
    /// <summary>A character, pressed with <paramref name="modifiers"/>.</summary>
    public static KeyStroke Of(char character, KeyModifiers modifiers = KeyModifiers.None) => new(null, character, modifiers);

    /// <summary>A named key, pressed with <paramref name="modifiers"/>.</summary>
    public static KeyStroke Named(string key, KeyModifiers modifiers = KeyModifiers.None) => new(key, '\0', modifiers);

    /// <inheritdoc/>
    public override string ToString()
    {
        var prefix = (Modifiers.HasFlag(KeyModifiers.Shift) ? "+" : string.Empty)
            + (Modifiers.HasFlag(KeyModifiers.Control) ? "^" : string.Empty)
            + (Modifiers.HasFlag(KeyModifiers.Alt) ? "%" : string.Empty);
        return prefix + (Key is { } key ? $"{{{key}}}" : Character.ToString());
    }
}

/// <summary>
/// Reads the keystrokes of a <c>SendKeys</c> string (<strong>MS-VBAL §6.1.2.8.2.5</strong>, and the syntax MS-VBA documents for it).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A character is the key that types it. <c>~</c> is <kbd>Enter</kbd>.</item>
/// <item><c>+</c>, <c>^</c> and <c>%</c> hold <kbd>Shift</kbd>, <kbd>Ctrl</kbd> and <kbd>Alt</kbd> down for the key that follows - or for every key of a group
/// in parentheses: <c>+(ec)</c> is <kbd>Shift</kbd> held down while <c>e</c> and <c>c</c> are pressed.</item>
/// <item>A key with no character is named in braces: <c>{ENTER}</c>, <c>{F5}</c>. So is a character that would mean something else: <c>{+}</c>, <c>{~}</c>,
/// <c>{(}</c>, <c>{{}</c>, <c>{}}</c>, and the brackets, <c>{[}</c> and <c>{]}</c>.</item>
/// <item>A count after the key in braces repeats it: <c>{LEFT 42}</c>, <c>{h 10}</c>.</item>
/// </list>
/// A string that does not follow this - an unknown key name, an unclosed brace or parenthesis, a modifier with nothing to modify - is refused
/// (<see cref="FormatException"/>), which the language reports as error 5.
/// </remarks>
public static class SendKeysParser
{
    /// <summary>
    /// The names of the keys a <c>SendKeys</c> string can name in braces, each with its canonical name: the one a <see cref="KeyStroke"/> carries.
    /// </summary>
    public static ImmutableDictionary<string, string> KeyNames { get; } = BuildKeyNames();

    private static ImmutableDictionary<string, string> BuildKeyNames()
    {
        var names = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        void Add(string canonical, params string[] aliases)
        {
            names[canonical] = canonical;
            foreach (var alias in aliases)
            {
                names[alias] = canonical;
            }
        }

        Add("BACKSPACE", "BS", "BKSP");
        Add("BREAK");
        Add("CAPSLOCK");
        Add("DELETE", "DEL");
        Add("DOWN");
        Add("END");
        Add("ENTER");
        Add("ESC");
        Add("HELP");
        Add("HOME");
        Add("INSERT", "INS");
        Add("LEFT");
        Add("NUMLOCK");
        Add("PGDN");
        Add("PGUP");
        Add("PRTSC");
        Add("RIGHT");
        Add("SCROLLLOCK");
        Add("TAB");
        Add("UP");
        Add("ADD");
        Add("SUBTRACT");
        Add("MULTIPLY");
        Add("DIVIDE");
        for (var function = 1; function <= 16; function++)
        {
            Add($"F{function}");
        }

        return names.ToImmutable();
    }

    /// <summary>
    /// The keystrokes of <paramref name="keys"/>, in the order they are pressed.
    /// </summary>
    /// <param name="keys">The <c>SendKeys</c> string.</param>
    /// <exception cref="FormatException"><paramref name="keys"/> is not in the syntax of <c>SendKeys</c>.</exception>
    public static IReadOnlyList<KeyStroke> Parse(string keys)
    {
        var strokes = new List<KeyStroke>();
        var position = 0;
        ParseSequence(keys, ref position, KeyModifiers.None, strokes, inGroup: false);
        return strokes;
    }

    private static void ParseSequence(string keys, ref int position, KeyModifiers held, List<KeyStroke> strokes, bool inGroup)
    {
        var pending = KeyModifiers.None;
        while (position < keys.Length)
        {
            var character = keys[position];
            switch (character)
            {
                case '+':
                    pending |= KeyModifiers.Shift;
                    position++;
                    continue;
                case '^':
                    pending |= KeyModifiers.Control;
                    position++;
                    continue;
                case '%':
                    pending |= KeyModifiers.Alt;
                    position++;
                    continue;
                case '(':
                    position++;
                    ParseSequence(keys, ref position, held | pending, strokes, inGroup: true);
                    pending = KeyModifiers.None;
                    continue;
                case ')':
                    if (!inGroup || pending != KeyModifiers.None)
                    {
                        throw Invalid(keys, position);
                    }

                    position++;
                    return;
                case '{':
                    strokes.AddRange(ParseBraced(keys, ref position, held | pending));
                    pending = KeyModifiers.None;
                    continue;
                case '}':
                    throw Invalid(keys, position);
                case '~':
                    strokes.Add(KeyStroke.Named("ENTER", held | pending));
                    break;
                default:
                    strokes.Add(KeyStroke.Of(character, held | pending));
                    break;
            }

            pending = KeyModifiers.None;
            position++;
        }

        // a group that is never closed, or a modifier with no key after it.
        if (inGroup || pending != KeyModifiers.None)
        {
            throw Invalid(keys, position);
        }
    }

    // {NAME}, {NAME n}, {c} or {c n}: the closing brace of a braced brace is the one after it ({}} is a closing brace).
    private static IEnumerable<KeyStroke> ParseBraced(string keys, ref int position, KeyModifiers modifiers)
    {
        var start = position + 1;
        var close = start < keys.Length && keys[start] == '}' ? keys.IndexOf('}', start + 1) : keys.IndexOf('}', start);
        if (close < 0 || close == start)
        {
            throw Invalid(keys, position);
        }

        var content = keys[start..close];
        position = close + 1;

        var count = 1;
        var space = content.LastIndexOf(' ');
        if (space > 0)
        {
            if (!int.TryParse(content[(space + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out count))
            {
                throw Invalid(keys, start);
            }

            content = content[..space];
        }

        KeyStroke stroke;
        if (content.Length == 1)
        {
            // a braced character is the character itself: {~} types a tilde, where a bare ~ is Enter.
            stroke = KeyStroke.Of(content[0], modifiers);
        }
        else if (KeyNames.TryGetValue(content, out var name))
        {
            stroke = KeyStroke.Named(name, modifiers);
        }
        else
        {
            throw Invalid(keys, start);
        }

        return Enumerable.Repeat(stroke, count);
    }

    private static FormatException Invalid(string keys, int position)
        => new($"'{keys}' is not a SendKeys string: nothing is understood at position {position + 1}.");
}
